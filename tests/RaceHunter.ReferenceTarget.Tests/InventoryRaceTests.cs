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
    internal string ConnectionString => database.GetConnectionString();

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceTarget"] = database.GetConnectionString(),
                ["DemoControl:Key"] = "test-control-key",
                ["ManualTarget:BearerToken"] = "manual-test-token"
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
    public async Task Absent_demo_control_configuration_disables_privileged_endpoint()
    {
        await using var disabledFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceTarget"] = fixture.ConnectionString
            })));
        using var disabledClient = disabledFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/demo/reset")
        {
            Content = JsonContent.Create(new { quantity = 1, mode = "fixed" })
        };
        request.Headers.Add("X-Demo-Control-Key", "local-demo-only");

        var response = await disabledClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Configured_demo_control_key_resets_inventory_and_mode()
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

    [Fact]
    public async Task Controlled_checkpoint_degrades_safely_when_only_one_actor_can_run()
    {
        await ResetAsync(1, "vulnerable");

        var first = await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-0", quantity = 1, checkpoint = "oversell" });
        var second = await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-1", quantity = 1, checkpoint = "oversell" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Controlled_checkpoints_are_isolated_and_remove_cancelled_waiters()
    {
        await ResetAsync(1, "vulnerable");
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client.PostAsJsonAsync(
                "/api/orders",
                new { actorId = "abandoned", quantity = 1, checkpoint = "oversell:run-a" },
                cancellation.Token));
        }
        await ResetAsync(1, "vulnerable");

        var first = Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-0", quantity = 1, checkpoint = "oversell:run-b" });
        await Task.Delay(50);
        Assert.False(first.IsCompleted);
        var second = Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-1", quantity = 1, checkpoint = "oversell:run-b" });
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
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

    [Fact]
    public async Task Controlled_manual_endpoint_observes_shared_transactional_state_only_after_concurrent_mutations()
    {
        await ResetAsync(1, "vulnerable");
        async Task<ManualOrderResult?> SendAsync(int actor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/manual/orders")
            {
                Content = JsonContent.Create(new { actorId = $"actor-{actor}", quantity = 1, checkpoint = "racehunter:shared-state", replayScope = "shared-state" })
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "manual-test-token");
            using var response = await Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<ManualOrderResult>();
        }

        var results = await Task.WhenAll(SendAsync(1), SendAsync(2));
        Assert.All(results, result => Assert.Equal(2, result!.ReservationCount));
        Assert.All(results, result => Assert.Equal(1, result!.ReservationCapacity));
    }

    [Fact]
    public async Task Durable_order_key_reuses_the_original_result_after_reset_without_reapplying_mutation()
    {
        await ResetAsync(1, "fixed");
        var first = await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor", quantity = 1, checkpoint = "none", idempotencyKey = "durable-order-1" });
        var original = await first.Content.ReadFromJsonAsync<OrderResult>();
        await ResetAsync(1, "fixed");

        var duplicate = await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor", quantity = 1, checkpoint = "none", idempotencyKey = "durable-order-1" });
        var replayed = await duplicate.Content.ReadFromJsonAsync<OrderResult>();
        var state = await Client.GetFromJsonAsync<InventoryState>("/api/inventory");

        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        Assert.Equal(original!.CorrelationId, replayed!.CorrelationId);
        Assert.True(replayed.Replayed);
        Assert.Equal(0, state!.SuccessfulOrders);
        Assert.Equal(1, state.Available);
    }

    [Fact]
    public async Task Durable_reset_key_is_applied_once()
    {
        await ResetWithKeyAsync(1, "fixed", "reset-once");
        await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor", quantity = 1, checkpoint = "none" });

        await ResetWithKeyAsync(1, "fixed", "reset-once");
        var state = await Client.GetFromJsonAsync<InventoryState>("/api/inventory");

        Assert.Equal(1, state!.SuccessfulOrders);
        Assert.Equal(0, state.Available);
    }

    [Fact]
    public async Task Replayed_order_still_participates_in_controlled_checkpoint_recovery()
    {
        const string scope = "partial-recovery-scope";
        await ResetWithKeyAsync(1, "vulnerable", "partial-recovery-reset", scope);
        var original = await Task.WhenAll(
            Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-1", quantity = 1, checkpoint = "oversell:original", idempotencyKey = "recovery-actor-1", replayScope = scope }),
            Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-2", quantity = 1, checkpoint = "oversell:original", idempotencyKey = "recovery-actor-2", replayScope = scope }));
        Assert.All(original, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await ResetWithKeyAsync(1, "vulnerable", "partial-recovery-reset", scope);

        var cachedActor = Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-1", quantity = 1, checkpoint = "oversell:recovery", idempotencyKey = "recovery-actor-1", replayScope = scope });
        await Task.Delay(50);
        Assert.False(cachedActor.IsCompleted);
        var cachedSecondActor = Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-2", quantity = 1, checkpoint = "oversell:recovery", idempotencyKey = "recovery-actor-2", replayScope = scope });
        var recovered = await Task.WhenAll(cachedActor, cachedSecondActor).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(recovered, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var state = await Client.GetFromJsonAsync<InventoryState>("/api/inventory");
        Assert.Equal(2, state!.SuccessfulOrders);
    }

    [Fact]
    public async Task Authenticated_order_status_reports_only_missing_durable_keys()
    {
        await ResetAsync(1, "fixed");
        await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor", quantity = 1, checkpoint = "none", idempotencyKey = "known-operation" });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/demo/order-status")
        {
            Content = JsonContent.Create(new { idempotencyKeys = new[] { "known-operation", "missing-operation" } })
        };
        request.Headers.Add("X-Demo-Control-Key", "test-control-key");

        using var response = await Client.SendAsync(request);
        var status = await response.Content.ReadFromJsonAsync<OrderStatus>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, status!.Missing);
    }

    [Fact]
    public async Task Replay_scope_does_not_make_sequential_vulnerable_orders_use_a_stale_snapshot()
    {
        const string scope = "sequential-scope";
        await ResetWithKeyAsync(1, "vulnerable", "sequential-reset", scope);

        var first = await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-1", quantity = 1, checkpoint = "", idempotencyKey = "sequential-1", replayScope = scope });
        var second = await Client.PostAsJsonAsync("/api/orders", new { actorId = "actor-2", quantity = 1, checkpoint = "", idempotencyKey = "sequential-2", replayScope = scope });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
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

    private async Task ResetWithKeyAsync(int quantity, string mode, string idempotencyKey)
        => await ResetWithKeyAsync(quantity, mode, idempotencyKey, null);

    private async Task ResetWithKeyAsync(int quantity, string mode, string idempotencyKey, string? replayScope)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/demo/reset")
        {
            Content = JsonContent.Create(new { quantity, mode })
        };
        request.Headers.Add("X-Demo-Control-Key", "test-control-key");
        request.Headers.Add("X-RaceHunter-Idempotency-Key", idempotencyKey);
        if (replayScope is not null) request.Headers.Add("X-RaceHunter-Replay-Scope", replayScope);
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed record InventoryState(int Available, int SuccessfulOrders, string Mode);
    private sealed record OrderResult(Guid CorrelationId, bool Replayed);
    private sealed record OrderStatus(int Missing);
    private sealed record ManualOrderResult(int ReservationCount, int ReservationCapacity);
}
