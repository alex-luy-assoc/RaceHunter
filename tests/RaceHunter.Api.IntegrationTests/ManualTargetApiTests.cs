using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Contracts;
using RaceHunter.Domain.Findings;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
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
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-admin");
        using var wrongResponse = await wrong.PostAsJsonAsync("/api/admin/targets", ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongResponse.StatusCode);
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
        Assert.Equal(ManualTargetIdempotencyModes.ReceiverKeyed, created.Operations.Single(operation => operation.IsSetup).IdempotencyMode);
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

    [Fact]
    public async Task Manual_resource_owner_is_required_for_every_plan_run_finding_and_replay_stage()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-admin");
        var target = await (await owner.PostAsJsonAsync("/api/admin/targets", ValidRequest("https://9.9.9.9", "9.9.9.9")))
            .Content.ReadFromJsonAsync<ManualTargetResponse>();
        using var missing = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-admin");
        await AssertDeniedAsync(missing, wrong, HttpMethod.Post, "/api/hunts", () => JsonContent.Create(new CreateHuntRequest(
            "manual owned invariant", 10, 5, 40, 5, 90, 1, target!.Id)));
        var hunt = await (await owner.PostAsJsonAsync("/api/hunts", new CreateHuntRequest(
            "manual owned invariant", 10, 5, 40, 5, 90, 1, target!.Id))).Content.ReadFromJsonAsync<HuntResponse>();

        await AssertDeniedAsync(missing, wrong, HttpMethod.Post, $"/api/hunts/{hunt!.Id}/plan");
        await AssertDeniedAsync(missing, wrong, HttpMethod.Get, $"/api/hunts/{hunt.Id}/plan");
        await AssertDeniedAsync(missing, wrong, HttpMethod.Get, $"/api/hunts/{hunt.Id}/events");
        (await owner.PostAsync($"/api/hunts/{hunt.Id}/plan", null)).EnsureSuccessStatusCode();

        var plan = new ScenarioPlan("owned-plan", "plan-v1", "prompt-v1", "test-model", "test-invocation",
            [new PlannedActor("actor-1", "place-order")],
            new PlannedInvariant("numeric-boundary", "successful-orders", 1, null, null, null),
            new PlannedStrategy("checkpoint-interleaving", 2, 42), 1, "{}");
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IHuntStore>().SavePlanAsync(hunt.Id, plan, DateTime.UtcNow, CancellationToken.None);

        var approvalBody = JsonContent.Create(new ApproveRunRequest(plan.PlanVersion, "owned-approval"));
        await AssertDeniedAsync(missing, wrong, HttpMethod.Post, $"/api/hunts/{hunt.Id}/runs", () => JsonContent.Create(new ApproveRunRequest(plan.PlanVersion, "denied")));
        using var approvalResponse = await owner.PostAsync($"/api/hunts/{hunt.Id}/runs", approvalBody);
        var approval = await approvalResponse.Content.ReadFromJsonAsync<ApprovalResponse>();
        approvalResponse.EnsureSuccessStatusCode();

        foreach (var route in new[] { $"/api/runs/{approval!.RunId}", $"/api/runs/{approval.RunId}/events", $"/api/runs/{approval.RunId}/traces", $"/api/cloud-proof?runId={approval.RunId}" })
            await AssertDeniedAsync(missing, wrong, HttpMethod.Get, route);
        await AssertDeniedAsync(missing, wrong, HttpMethod.Post, $"/api/runs/{approval.RunId}/cancel");

        var findingId = Guid.NewGuid();
        var artifact = ReplayArtifact.Create(Guid.NewGuid(), findingId, "manual-scenario-v1", "manual-invariant-v1", "manual-owned-snapshot",
            "checkpoint-interleaving", 42, [new ReplayStep(1, "place-order", "place-order", 0), new ReplayStep(2, "place-order", "place-order", 0)], "{}", DateTime.UtcNow);
        var finding = Finding.CreateReference(findingId, approval.RunId, "manual-invariant-v1",
            new InvariantResult(InvariantOutcome.Fail, ["trace:owned"], "owned failure"),
            Enumerable.Range(1, 3).Select(index => new ReproductionAttempt(index, InvariantOutcome.Fail, [$"trace:owned:{index}"])).ToArray(),
            artifact, DateTime.UtcNow, "sanitized");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var findings = scope.ServiceProvider.GetRequiredService<IFindingStore>();
            await scope.ServiceProvider.GetRequiredService<IReplayStore>().AddArtifactAsync(artifact, CancellationToken.None);
            await findings.AddAsync(finding, CancellationToken.None);
        }
        await AssertDeniedAsync(missing, wrong, HttpMethod.Get, $"/api/findings/{findingId}");
        await AssertDeniedAsync(missing, wrong, HttpMethod.Post, $"/api/findings/{findingId}/replays",
            () => JsonContent.Create(new VerifyFixRequest("owned-replay")));
    }

    [Fact]
    public async Task Safety_rejections_are_persisted_with_sanitized_category_and_visible_after_refresh()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-admin");
        using var rejected = await owner.PostAsJsonAsync("/api/admin/targets", ValidRequest("http://169.254.169.254", "169.254.169.254"));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var audits = await owner.GetFromJsonAsync<SecurityAuditEvent[]>("/api/admin/audit-events");
        Assert.Contains(audits!, item => item.Stage == "configuration" && item.Outcome == "rejected");
        var safety = audits!.First(item => item.Stage == "configuration" && item.Outcome == "rejected");
        Assert.NotEmpty(safety.Category);
        Assert.DoesNotContain("169.254.169.254", safety.SanitizedDetail, StringComparison.Ordinal);
        using var missing = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-admin");
        await AssertDeniedAsync(missing, wrong, HttpMethod.Get, "/api/admin/audit-events");
    }

    private static async Task AssertDeniedAsync(HttpClient missing, HttpClient wrong, HttpMethod method, string route,
        Func<HttpContent?>? content = null)
    {
        using var missingRequest = new HttpRequestMessage(method, route) { Content = content?.Invoke() };
        using var wrongRequest = new HttpRequestMessage(method, route) { Content = content?.Invoke() };
        using var missingResponse = await missing.SendAsync(missingRequest);
        using var wrongResponse = await wrong.SendAsync(wrongRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongResponse.StatusCode);
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
            new ManualTargetOperationRequest("reset", "POST", "/reset", "{\"quantity\":1}", new Dictionary<string, string>(), true,
                new Dictionary<string, string>(), ManualTargetIdempotencyModes.ReceiverKeyed),
            new ManualTargetOperationRequest("place-order", "POST", "/orders",
                "{\"actorId\":\"{{actorId}}\",\"runId\":\"{{runId}}\"}",
                new Dictionary<string, string> { ["successful-orders"] = "$.successfulOrders", ["inventory-capacity"] = "$.inventoryCapacity" })
        ],
        ["$.customer.token"]);
}
