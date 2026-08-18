using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Projects;
using RaceHunter.Domain.Projects;
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

    private RaceHunterDbContext CreateContext() => new(
        new DbContextOptionsBuilder<RaceHunterDbContext>().UseNpgsql(fixture.Database.GetConnectionString()).Options);
}
