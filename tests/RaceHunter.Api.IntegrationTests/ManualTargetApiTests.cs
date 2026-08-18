using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RaceHunter.Contracts;
using Xunit;

namespace RaceHunter.Api.IntegrationTests;

public sealed class ManualTargetApiTests(ApiDatabaseFixture fixture) : IClassFixture<ApiDatabaseFixture>
{
    [Fact]
    public async Task Public_sandbox_cannot_raise_actor_request_model_duration_or_retry_budgets()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/hunts", new CreateHuntRequest(
            "oversell", 100, 5, 100, 6, 120, 2));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Authenticated_admin_can_use_the_one_hundred_actor_engine_with_bounded_concurrency()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-admin");

        using var response = await client.PostAsJsonAsync("/api/hunts", new CreateHuntRequest(
            "authorized target rule", 100, 7, 100, 5, 90, 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Public_sandbox_hides_manual_target_configuration_without_admin_authentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/admin/targets", ValidRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_admin_can_persist_only_safe_reference_based_manual_target()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-admin");

        using var response = await client.PostAsJsonAsync("/api/admin/targets", ValidRequest());
        var created = await response.Content.ReadFromJsonAsync<ManualTargetResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal("8.8.8.8", created.Host);
        Assert.StartsWith("projects/", created.CredentialReference, StringComparison.Ordinal);
        Assert.Equal("place-order", created.Operations.Single(operation => !operation.IsSetup).Id);
        Assert.Equal("$.successfulOrders", created.Operations.Single(operation => !operation.IsSetup).ObservationPaths["successful-orders"]);
        Assert.DoesNotContain("local-admin", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_admin_cannot_submit_raw_credential_value()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-admin");
        var request = ValidRequest() with { CredentialReference = "raw-secret-value" };

        using var response = await client.PostAsJsonAsync("/api/admin/targets", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("raw-secret-value", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_admin_can_bind_a_hunt_to_an_authorized_manual_target_snapshot()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-admin");
        var configured = await (await client.PostAsJsonAsync("/api/admin/targets", ValidRequest("https://1.1.1.1", "1.1.1.1")))
            .Content.ReadFromJsonAsync<ManualTargetResponse>();

        using var response = await client.PostAsJsonAsync("/api/hunts", new CreateHuntRequest(
            "manual invariant", 10, 5, 40, 5, 90, 1, configured!.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Cloud_proof_rejects_an_unknown_caller_supplied_run_id()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/cloud-proof?runId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:RaceHunter", fixture.Database.GetConnectionString());
            builder.UseSetting("PubSub:ProjectId", string.Empty);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RaceHunter"] = fixture.Database.GetConnectionString(),
                ["ManualTargets:AdminToken"] = "local-admin"
            }));
        });

    private static ConfigureManualTargetRequest ValidRequest(string baseUrl = "https://8.8.8.8", string host = "8.8.8.8") => new(
        baseUrl,
        [host],
        true,
        "projects/demo-project/secrets/orders-token/versions/latest",
        [
            new ManualTargetOperationRequest("reset", "POST", "/reset", "{\"quantity\":1}", new Dictionary<string, string>(), true),
            new ManualTargetOperationRequest("place-order", "POST", "/orders",
                "{\"actorId\":\"{{actorId}}\",\"runId\":\"{{runId}}\"}",
                new Dictionary<string, string> { ["successful-orders"] = "$.successfulOrders", ["inventory-capacity"] = "$.inventoryCapacity" })
        ],
        ["$.customer.token"]);
}
