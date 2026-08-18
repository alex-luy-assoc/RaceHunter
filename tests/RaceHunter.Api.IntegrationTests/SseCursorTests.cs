using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Runs;
using RaceHunter.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RaceHunter.Api.IntegrationTests;

public sealed class ApiDatabaseFixture : IAsyncLifetime
{
    internal readonly PostgreSqlContainer Database = new PostgreSqlBuilder("postgres:17-alpine@sha256:18cfe3ef5e6815560c98237d6216d1e5119702fb0f3894c8785dd58b8bbe5d73")
        .WithDatabase("racehunter_api_test")
        .WithUsername("racehunter")
        .WithPassword("racehunter_test")
        .Build();

    public Task InitializeAsync() => Database.StartAsync();
    public Task DisposeAsync() => Database.DisposeAsync().AsTask();
}

public sealed class SseCursorTests(ApiDatabaseFixture fixture) : IClassFixture<ApiDatabaseFixture>
{
    [Fact]
    public async Task Json_refresh_returns_only_events_after_durable_cursor()
    {
        var run = await CreateTerminalRunAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync($"/api/runs/{run.Id}/events?after=1");

        Assert.DoesNotContain("first-event", json, StringComparison.Ordinal);
        Assert.Contains("second-event", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sse_refresh_honors_last_event_id_and_emits_durable_cursor()
    {
        var run = await CreateTerminalRunAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{run.Id}/events?after=0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("Last-Event-ID", "1");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain("first-event", body, StringComparison.Ordinal);
        Assert.Contains("id: 2", body, StringComparison.Ordinal);
        Assert.Contains("event: second-event", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RunStatus.Reproducing)]
    [InlineData(RunStatus.Minimizing)]
    public async Task Run_refresh_reconstructs_the_current_persisted_finding_phase(RunStatus phase)
    {
        var run = await CreateActivePhaseRunAsync(phase);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync($"/api/runs/{run.Id}");

        Assert.Contains($"\"status\":\"{phase}\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cursor_replay_preserves_reproduction_before_minimization()
    {
        var run = await CreateActivePhaseRunAsync(RunStatus.Minimizing);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync($"/api/runs/{run.Id}/events?after=1");
        var reproduction = json.IndexOf("reproduction-started", StringComparison.Ordinal);
        var minimization = json.IndexOf("minimization-started", StringComparison.Ordinal);

        Assert.True(reproduction >= 0);
        Assert.True(minimization > reproduction);
    }

    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:RaceHunter", fixture.Database.GetConnectionString());
            builder.UseSetting("PubSub:ProjectId", string.Empty);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RaceHunter"] = fixture.Database.GetConnectionString()
            }));
        });

    private async Task<ExperimentRun> CreateTerminalRunAsync()
    {
        await using var context = new RaceHunterDbContext(new DbContextOptionsBuilder<RaceHunterDbContext>()
            .UseNpgsql(fixture.Database.GetConnectionString()).Options);
        await context.Database.MigrateAsync();
        var store = new RunStore(context);
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UtcNow);
        await store.AddAsync(run, CancellationToken.None);
        run.Start(DateTime.UtcNow);
        run.AppendEvent("first-event", "one", DateTime.UtcNow);
        run.AppendEvent("second-event", "two", DateTime.UtcNow);
        run.Complete(DateTime.UtcNow);
        await store.SaveAsync(run, CancellationToken.None);
        return run;
    }

    private async Task<ExperimentRun> CreateActivePhaseRunAsync(RunStatus phase)
    {
        await using var context = new RaceHunterDbContext(new DbContextOptionsBuilder<RaceHunterDbContext>()
            .UseNpgsql(fixture.Database.GetConnectionString()).Options);
        await context.Database.MigrateAsync();
        var store = new RunStore(context);
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UtcNow);
        await store.AddAsync(run, CancellationToken.None);
        run.Start(DateTime.UtcNow);
        run.AppendEvent("campaign-started", "Campaign started.", DateTime.UtcNow);
        run.BeginReproduction(DateTime.UtcNow);
        if (phase == RunStatus.Minimizing) run.BeginMinimization(DateTime.UtcNow);
        await store.SaveAsync(run, CancellationToken.None);
        return run;
    }
}
