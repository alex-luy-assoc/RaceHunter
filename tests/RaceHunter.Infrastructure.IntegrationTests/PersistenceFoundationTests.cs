using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Projects;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Projects;
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

    private RaceHunterDbContext CreateContext() => new(
        new DbContextOptionsBuilder<RaceHunterDbContext>().UseNpgsql(fixture.Database.GetConnectionString()).Options);
}
