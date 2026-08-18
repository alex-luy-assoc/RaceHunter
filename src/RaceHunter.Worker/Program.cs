using RaceHunter.Application.Abstractions;
using RaceHunter.Concurrency.Execution;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Contracts;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Common;
using RaceHunter.Domain.Invariants;
using RaceHunter.Infrastructure.Persistence;
using RaceHunter.Worker.Execution;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("RaceHunter")
    ?? throw new InvalidOperationException("ConnectionStrings:RaceHunter is required.");
builder.Services.AddRaceHunterPersistence(connectionString);
builder.Services.AddSingleton(new ConcurrencyScheduler(
    builder.Configuration.GetValue("Concurrency:Global", 32),
    builder.Configuration.GetValue("Concurrency:ReferenceTarget", 10)));
builder.Services.AddScoped<ManualHuntExecutor>(provider => new ManualHuntExecutor(
    provider.GetRequiredService<ConcurrencyScheduler>(),
    provider.GetRequiredService<IRunStore>(),
    provider.GetRequiredService<IRunCancellationProbe>(),
    provider.GetRequiredService<ITraceStore>(),
    provider.GetRequiredService<IRunAttemptStore>()));
builder.Services.AddHttpClient<ReferenceInventoryTargetClient>((services, client) =>
{
    var baseUrl = services.GetRequiredService<IConfiguration>()["ReferenceTarget:BaseUrl"]
        ?? throw new InvalidOperationException("ReferenceTarget:BaseUrl is required.");
    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHealthChecks();
var app = builder.Build();
await app.Services.ApplyRaceHunterMigrationsAsync();
app.MapHealthChecks("/healthz");
app.MapPost("/internal/manual-hunts", async (
    ManualInventoryHuntRequest input,
    ManualHuntExecutor executor,
    ReferenceInventoryTargetClient target,
    CancellationToken cancellationToken) =>
{
    try
    {
        var schedule = Enum.Parse<ScheduleKind>(input.Schedule, ignoreCase: true);
        var budget = new ExperimentBudget(
            input.ActorCount,
            input.MaxConcurrency,
            input.MaxRequests,
            0,
            TimeSpan.FromSeconds(input.MaxDurationSeconds),
            0);
        var request = new ManualHuntRequest(
            input.RunId ?? Guid.NewGuid(),
            budget,
            schedule,
            input.Seed,
            new NumericBoundaryInvariant("successful-orders", input.MaximumSuccessfulOrders));
        var result = await executor.ExecuteAsync(request, (actor, token) => target.PlaceOrderAsync(request.RunId, actor, token), cancellationToken);
        return Results.Ok(new ManualInventoryHuntResponse(result.Id, result.Status.ToString(), result.InvariantOutcome?.ToString()));
    }
    catch (Exception exception) when (exception is DomainException or ArgumentException)
    {
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid manual hunt", detail: exception.Message);
    }
});
app.Run();

public partial class Program;
