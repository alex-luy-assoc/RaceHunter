using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RaceHunter.Application.Replays;
using RaceHunter.Contracts;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Findings;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using RaceHunter.Domain.Runs;
using RaceHunter.Infrastructure.Persistence;
using Xunit;

namespace RaceHunter.Api.IntegrationTests;

public sealed class FindingApiTests(ApiDatabaseFixture fixture) : IClassFixture<ApiDatabaseFixture>
{
    [Fact]
    public async Task Get_finding_returns_deterministic_truth_replay_and_judge_evidence_projection()
    {
        var finding = await SeedFindingAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<FindingResponse>($"/api/findings/{finding.Id}");

        Assert.NotNull(response);
        Assert.Equal("Race condition verified — reproduced 3/3 and minimized to 2 actors.", response.SuccessMessage);
        Assert.Equal("Fail", response.InvariantOutcome);
        Assert.Equal(3, response.Reproductions.Count);
        Assert.Equal(2, response.ReplayArtifact.ActorCount);
        Assert.NotEmpty(response.Timeline);
        Assert.NotEmpty(response.AgentActivity);
    }

    [Fact]
    public async Task Get_unknown_finding_returns_problem_details_not_found()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/findings/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Verify_fix_returns_accepted_fixed_pass_and_preserves_vulnerable_failure()
    {
        var finding = await SeedFindingAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync($"/api/findings/{finding.Id}/replays", new VerifyFixRequest("verify-api-1"));
        var comparison = await response.Content.ReadFromJsonAsync<ReplayComparisonResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(comparison);
        Assert.Equal("Fail", comparison.VulnerableOutcome);
        Assert.Equal("Pass", comparison.FixedOutcome);
        Assert.Equal("verify-api-1", comparison.IdempotencyKey);
    }

    [Fact]
    public async Task Concurrent_verify_fix_requests_execute_the_target_once_and_return_the_stored_winner()
    {
        var finding = await SeedFindingAsync();
        var execution = new CountingPassingReplayExecution();
        await using var factory = CreateFactory(execution);
        using var client = factory.CreateClient();

        var first = client.PostAsJsonAsync($"/api/findings/{finding.Id}/replays", new VerifyFixRequest("concurrent-a"));
        await execution.WaitUntilStartedAsync();
        var second = client.PostAsJsonAsync($"/api/findings/{finding.Id}/replays", new VerifyFixRequest("concurrent-b"));
        await Task.Delay(250);

        Assert.False(second.IsCompleted);
        Assert.Equal(1, execution.Calls);
        execution.Release();
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.Equal(1, execution.Calls);
        await using var context = new RaceHunterDbContext(new DbContextOptionsBuilder<RaceHunterDbContext>()
            .UseNpgsql(fixture.Database.GetConnectionString()).Options);
        Assert.Equal(1, await context.ReplayAttempts.CountAsync(item => item.ArtifactId == finding.ReplayArtifactId && item.TargetMode == "Fixed"));
    }

    [Fact]
    public async Task Verify_fix_rejects_unknown_finding_and_empty_idempotency_key_with_problem_details()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var missing = await client.PostAsJsonAsync($"/api/findings/{Guid.NewGuid()}/replays", new VerifyFixRequest("missing"));
        var finding = await SeedFindingAsync();
        using var invalid = await client.PostAsJsonAsync($"/api/findings/{finding.Id}/replays", new VerifyFixRequest(""));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Verify_fix_maps_worker_transport_failure_to_recoverable_problem_details()
    {
        var finding = await SeedFindingAsync();
        await using var factory = CreateFactory(new ThrowingReplayExecution());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync($"/api/findings/{finding.Id}/replays", new VerifyFixRequest("unavailable"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private WebApplicationFactory<Program> CreateFactory(IReplayExecution? replayExecution = null) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:RaceHunter", fixture.Database.GetConnectionString());
            builder.UseSetting("PubSub:ProjectId", string.Empty);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RaceHunter"] = fixture.Database.GetConnectionString()
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IReplayExecution>();
                services.AddSingleton(replayExecution ?? new PassingReplayExecution());
            });
        });

    private async Task<Finding> SeedFindingAsync()
    {
        await using var context = new RaceHunterDbContext(new DbContextOptionsBuilder<RaceHunterDbContext>()
            .UseNpgsql(fixture.Database.GetConnectionString()).Options);
        await context.Database.MigrateAsync();
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, Utc(0));
        var runs = new RunStore(context);
        await runs.AddAsync(run, CancellationToken.None);
        run.Start(Utc(1));
        await runs.SaveAsync(run, CancellationToken.None);
        context.AgentIterations.Add(new AgentIterationPersistenceRecord
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Iteration = 1,
            EvidenceSummary = "trace refs only",
            Action = "StartMinimization",
            RationaleSummary = "Reduce actors",
            ModelId = "gemini-3.5-flash",
            SchemaVersion = "strategy-v1",
            ModelInvocationId = "model-1",
            OccurredAtUtc = Utc(2)
        });
        var attemptId = Guid.NewGuid();
        context.RunAttempts.Add(new RunAttemptRecord
        {
            Id = attemptId,
            RunId = run.Id,
            Strategy = "checkpoint-interleaving",
            Seed = 1729,
            Status = "Completed",
            StartedAtUtc = Utc(1),
            CompletedAtUtc = Utc(2)
        });
        context.TraceEvents.Add(new TraceEventRecord
        {
            RunId = run.Id,
            AttemptId = attemptId,
            Sequence = 1,
            ActorId = 1,
            StepId = "place-order",
            Kind = "response-success",
            RequestId = "request-a",
            OccurredAtUtc = Utc(2)
        });
        await context.SaveChangesAsync();

        var findingId = Guid.NewGuid();
        var artifact = ReplayArtifact.Create(Guid.NewGuid(), findingId, "scenario-v1", "invariant-v1", "inventory:one-unit",
            "checkpoint-interleaving", 1729,
            [new ReplayStep(1, "place-order", "place-order", 0), new ReplayStep(2, "place-order", "place-order", 0)],
            "{\"quantity\":1}", Utc(4));
        var finding = Finding.CreateReference(findingId, run.Id, "invariant-v1",
            new InvariantResult(InvariantOutcome.Fail, ["trace:1"], "oversell"),
            Enumerable.Range(1, 3).Select(index => new ReproductionAttempt(index, InvariantOutcome.Fail, [$"trace:r{index}"])).ToArray(),
            artifact, Utc(5), "Gemini interpretation");
        var store = new FindingStore(context);
        await store.AddArtifactAsync(artifact, CancellationToken.None);
        await store.AddAsync(finding, CancellationToken.None);
        await store.AddAttemptAsync(ReplayAttempt.Complete(Guid.NewGuid(), artifact.Id, ReplayTargetMode.Vulnerable,
            InvariantOutcome.Fail, ["trace:vulnerable"], artifact.Fingerprint, "original-vulnerable", Utc(6)), CancellationToken.None);
        return finding;
    }

    private static DateTime Utc(int seconds) => new(2026, 8, 18, 12, 0, seconds, DateTimeKind.Utc);

    private sealed class PassingReplayExecution : IReplayExecution
    {
        public Task<ReplayAttempt> ExecuteAsync(ReplayArtifact artifact, ReplayTargetMode targetMode, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(ReplayAttempt.Complete(Guid.NewGuid(), artifact.Id, targetMode, InvariantOutcome.Pass,
                ["trace:fixed"], artifact.Fingerprint, idempotencyKey, Utc(8)));
    }

    private sealed class CountingPassingReplayExecution : IReplayExecution
    {
        private int calls;
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref calls);
        public Task WaitUntilStartedAsync() => started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void Release() => released.TrySetResult();

        public async Task<ReplayAttempt> ExecuteAsync(ReplayArtifact artifact, ReplayTargetMode targetMode, string idempotencyKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
            return ReplayAttempt.Complete(Guid.NewGuid(), artifact.Id, targetMode, InvariantOutcome.Pass,
                ["trace:fixed"], artifact.Fingerprint, idempotencyKey, Utc(8));
        }
    }

    private sealed class ThrowingReplayExecution : IReplayExecution
    {
        public Task<ReplayAttempt> ExecuteAsync(ReplayArtifact artifact, ReplayTargetMode targetMode, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new HttpRequestException("worker unavailable");
    }
}
