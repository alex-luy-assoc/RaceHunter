using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Projects;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Projects;
using RaceHunter.Domain.Replays;
using RaceHunter.Domain.Runs;
using RaceHunter.Domain.Tracing;
using RaceHunter.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RaceHunter.Infrastructure.IntegrationTests;

public sealed class PersistenceDatabaseFixture : IAsyncLifetime
{
    internal readonly PostgreSqlContainer Database = new PostgreSqlBuilder("postgres:17-alpine@sha256:18cfe3ef5e6815560c98237d6216d1e5119702fb0f3894c8785dd58b8bbe5d73")
        .WithDatabase("racehunter_test")
        .WithUsername("racehunter")
        .WithPassword("racehunter_test")
        .Build();

    public Task InitializeAsync() => Database.StartAsync();
    public Task DisposeAsync() => Database.DisposeAsync().AsTask();
}

public sealed class PersistenceFoundationTests(PersistenceDatabaseFixture fixture) : IClassFixture<PersistenceDatabaseFixture>
{

    [Fact]
    public async Task Initial_migration_applies_to_empty_postgresql_database()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        Assert.Contains("202608180001_InitialCreate", await context.Database.GetAppliedMigrationsAsync());
        Assert.Contains("202608180002_AddRunEvidence", await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Project_repository_round_trips_aggregate()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new ProjectRepository(context);
        var projectName = $"Inventory correctness {Guid.NewGuid():N}";
        var project = Project.Create(Guid.Parse("a7fb2428-d948-46bc-9c39-c54e4dd3d275"), projectName);

        await repository.AddAsync(project, CancellationToken.None);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loaded = await repository.GetAsync(project.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(project.Id, loaded.Id);
        Assert.Equal(projectName, loaded.Name);
        Assert.Equal(project.CreatedAtUtc, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task Duplicate_project_name_is_rejected_by_database_constraint()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new ProjectRepository(context);
        var projectName = $"Duplicate project {Guid.NewGuid():N}";
        await repository.AddAsync(Project.Create(Guid.NewGuid(), projectName), CancellationToken.None);
        await context.SaveChangesAsync();
        await repository.AddAsync(Project.Create(Guid.NewGuid(), projectName), CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Project_timestamp_is_stored_as_utc()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new ProjectRepository(context);
        var project = Project.Create(Guid.NewGuid(), $"UTC project {Guid.NewGuid():N}");
        await repository.AddAsync(project, CancellationToken.None);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loaded = await repository.GetAsync(project.Id, CancellationToken.None);
        Assert.Equal(DateTimeKind.Utc, loaded!.CreatedAtUtc.Kind);
    }

    [Fact]
    public async Task Run_store_round_trips_durable_lifecycle_and_cursor_paged_events()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var store = new RunStore(context);
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UtcNow);
        await store.AddAsync(run, CancellationToken.None);
        run.Start(DateTime.UtcNow);
        run.AppendEvent("attempt-started", "Attempt started", DateTime.UtcNow);
        run.AppendEvent("target-call-completed", "Actor completed", DateTime.UtcNow);
        await store.SaveAsync(run, CancellationToken.None);
        context.ChangeTracker.Clear();

        var loaded = await store.GetAsync(run.Id, CancellationToken.None);
        var afterFirst = await store.GetEventsAsync(run.Id, 1, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(RunStatus.Running, loaded.Status);
        Assert.Equal(2, loaded.Events.Count);
        Assert.Equal("target-call-completed", Assert.Single(afterFirst).Kind);
    }

    [Fact]
    public async Task Trace_store_returns_evidence_in_sequence_order()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var store = new RunStore(context);
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run = ExperimentRun.Queue(runId, ExperimentBudget.PublicSandbox, DateTime.UtcNow);
        await store.AddAsync(run, CancellationToken.None);
        await store.AddAsync(RunAttempt.Start(attemptId, runId, "SimultaneousStart", 42, DateTime.UtcNow), CancellationToken.None);
        await store.AppendAsync(new TraceEvent(2, runId, attemptId, 2, "order", "response", "request-2", DateTime.UtcNow), CancellationToken.None);
        await store.AppendAsync(new TraceEvent(1, runId, attemptId, 1, "order", "response", "request-1", DateTime.UtcNow), CancellationToken.None);
        context.ChangeTracker.Clear();

        var traces = await store.GetAsync(runId, 0, CancellationToken.None);

        Assert.Equal([1L, 2L], traces.Select(item => item.Sequence));
    }

    [Fact]
    public async Task Replay_artifact_fingerprint_survives_postgresql_timestamp_precision()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var store = new FindingStore(context);
        var findingId = Guid.NewGuid();
        var artifact = ReplayArtifact.Create(
            Guid.NewGuid(),
            findingId,
            "scenario-v1",
            "invariant-v1",
            "inventory:one-unit",
            "checkpoint-interleaving",
            1729,
            [new ReplayStep(1, "place-order", "place-order", 0), new ReplayStep(2, "place-order", "place-order", 0)],
            "{\"quantity\":1}",
            DateTime.UtcNow);

        await store.AddArtifactAsync(artifact, CancellationToken.None);
        context.ChangeTracker.Clear();

        var loaded = await store.GetArtifactAsync(artifact.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(artifact.Fingerprint, loaded.Fingerprint);
        Assert.Equal(artifact.CreatedAtUtc, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task Finding_probe_checkpoint_is_durably_keyed_and_duplicate_delivery_reuses_the_same_outcome()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UtcNow);
        await new RunStore(context).AddAsync(run, CancellationToken.None);
        var store = new FindingProbeCheckpointStore(context);
        var checkpoint = new FindingProbeCheckpoint(
            run.Id, "reproduction:1", "reproduction", 1, "{\"strategy\":\"checkpoint-interleaving\"}",
            InvariantOutcome.Fail.ToString(), ["trace:1", "trace:2"], 2, DateTime.UtcNow);

        await store.SaveAsync(checkpoint, CancellationToken.None);
        context.ChangeTracker.Clear();
        await store.SaveAsync(checkpoint, CancellationToken.None);
        context.ChangeTracker.Clear();
        var loaded = await store.GetAsync(run.Id, "reproduction:1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(checkpoint.CandidateJson), JsonNode.Parse(loaded.CandidateJson)));
        Assert.Equal(checkpoint.TraceReferences, loaded.TraceReferences);
        Assert.Equal(1, await context.FindingProbeCheckpoints.CountAsync(item => item.RunId == run.Id));
    }

    [Fact]
    public async Task Manual_setup_claim_persists_retry_ambiguity_completion_and_physical_budget()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var run = ExperimentRun.Queue(Guid.NewGuid(), new ExperimentBudget(4, 4, 4, 1, TimeSpan.FromMinutes(1), 1), DateTime.UtcNow);
        await new RunStore(context).AddAsync(run, CancellationToken.None);
        var target = new ManualTargetSnapshot(Guid.NewGuid(), new Uri($"https://{Guid.NewGuid():N}.example.test"), "api.example.test",
            "projects/demo/secrets/token/versions/latest",
            [new ManualTargetOperation("setup", "POST", "/reset", "{}", new Dictionary<string, string>(), true,
                new Dictionary<string, string>(), ManualTargetIdempotencyModes.ReceiverKeyed),
             new ManualTargetOperation("execute", "POST", "/execute", "{}", new Dictionary<string, string> { ["count"] = "$.count" })],
            [], DateTime.UtcNow, "owner");
        await new ManualTargetStore(context).AddAsync(target, CancellationToken.None);
        var store = new ManualSetupExecutionStore(context);

        var first = await store.ReserveAsync(run.Id, target.Id, "campaign:1", "setup", ManualTargetIdempotencyModes.ReceiverKeyed, CancellationToken.None);
        context.ChangeTracker.Clear();
        var retry = await store.ReserveAsync(run.Id, target.Id, "campaign:1", "setup", ManualTargetIdempotencyModes.ReceiverKeyed, CancellationToken.None);
        await store.CompleteAsync(run.Id, "campaign:1", "setup", CancellationToken.None);
        context.ChangeTracker.Clear();
        var completed = await store.ReserveAsync(run.Id, target.Id, "campaign:1", "setup", ManualTargetIdempotencyModes.ReceiverKeyed, CancellationToken.None);
        var unsafeFirst = await store.ReserveAsync(run.Id, target.Id, "probe:unsafe", "setup", ManualTargetIdempotencyModes.None, CancellationToken.None);
        context.ChangeTracker.Clear();
        var unsafeRecovery = await store.ReserveAsync(run.Id, target.Id, "probe:unsafe", "setup", ManualTargetIdempotencyModes.None, CancellationToken.None);

        Assert.Equal(ManualSetupClaimDisposition.Send, first.Disposition);
        Assert.Equal(ManualSetupClaimDisposition.Send, retry.Disposition);
        Assert.Equal(2, retry.PhysicalRequestsReserved);
        Assert.Equal(ManualSetupClaimDisposition.Completed, completed.Disposition);
        Assert.Equal(ManualSetupClaimDisposition.Send, unsafeFirst.Disposition);
        Assert.Equal(ManualSetupClaimDisposition.Ambiguous, unsafeRecovery.Disposition);
        Assert.Equal(1, unsafeRecovery.PhysicalRequestsReserved);
        Assert.True(await store.CanStartAsync(run.Id, 1, CancellationToken.None));
        Assert.False(await store.CanStartAsync(run.Id, 2, CancellationToken.None));
    }

    private RaceHunterDbContext CreateContext() => new(
        new DbContextOptionsBuilder<RaceHunterDbContext>().UseNpgsql(fixture.Database.GetConnectionString()).Options);
}
