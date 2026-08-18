using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace RaceHunter.ReferenceTarget.Tests;

public sealed class ReferenceTargetFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine@sha256:18cfe3ef5e6815560c98237d6216d1e5119702fb0f3894c8785dd58b8bbe5d73")
        .WithDatabase("target_test")
        .WithUsername("racehunter")
        .WithPassword("racehunter_test")
        .Build();
    private WebApplicationFactory<Program>? factory;
    internal HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceTarget"] = database.GetConnectionString(),
                ["DemoControl:Key"] = "test-control-key"
            })));
        Client = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await database.DisposeAsync();
    }
}

public sealed class InventoryRaceTests(ReferenceTargetFixture fixture) : IClassFixture<ReferenceTargetFixture>
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Health_endpoint_reports_healthy() =>
        Assert.Equal("Healthy", await Client.GetStringAsync("/healthz"));

    [Fact]
    public async Task Demo_control_requires_key()
    {
        var response = await Client.PostAsJsonAsync("/demo/reset", new { quantity = 1, mode = "vulnerable" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reset_seeds_inventory_and_mode()
    {
        await ResetAsync(3, "vulnerable");
        var state = await Client.GetFromJsonAsync<InventoryState>("/api/inventory");
        Assert.Equal(3, state!.Available);
        Assert.Equal("vulnerable", state.Mode);
        Assert.Equal(0, state.SuccessfulOrders);
    }

    [Fact]
    public async Task Vulnerable_mode_allows_controlled_oversell()
    {
        await ResetAsync(1, "vulnerable");
        var requests = Enumerable.Range(0, 2).Select(actor =>
            Client.PostAsJsonAsync("/api/orders", new { actorId = $"actor-{actor}", quantity = 1, checkpoint = "oversell" }));
        var responses = await Task.WhenAll(requests);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var state = await Client.GetFromJsonAsync<InventoryState>("/api/inventory");
        Assert.Equal(2, state!.SuccessfulOrders);
        Assert.Equal(-1, state.Available);
    }

    [Fact]
    public async Task Fixed_mode_allows_only_one_order_for_one_unit()
    {
        await ResetAsync(1, "fixed");
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(actor =>
            Client.PostAsJsonAsync("/api/orders", new { actorId = $"actor-{actor}", quantity = 1, checkpoint = "oversell" })));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var state = await Client.GetFromJsonAsync<InventoryState>("/api/inventory");
        Assert.Equal(1, state!.SuccessfulOrders);
        Assert.Equal(0, state.Available);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Order_rejects_non_positive_quantity(int quantity)
    {
        await ResetAsync(1, "fixed");
        var response = await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor", quantity, checkpoint = "none" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Order_response_contains_correlation_id()
    {
        await ResetAsync(1, "fixed");
        var response = await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor", quantity = 1, checkpoint = "none" });
        var order = await response.Content.ReadFromJsonAsync<OrderResult>();
        Assert.NotEqual(Guid.Empty, order!.CorrelationId);
    }

    private async Task ResetAsync(int quantity, string mode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/demo/reset")
        {
            Content = JsonContent.Create(new { quantity, mode })
        };
        request.Headers.Add("X-Demo-Control-Key", "test-control-key");
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed record InventoryState(int Available, int SuccessfulOrders, string Mode);
    private sealed record OrderResult(Guid CorrelationId);
}
