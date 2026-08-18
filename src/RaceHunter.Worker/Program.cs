using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Application.Replays;
using RaceHunter.Concurrency.Execution;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Contracts;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Common;
using RaceHunter.Domain.Invariants;
using RaceHunter.Gemini;
using RaceHunter.Infrastructure.Messaging;
using RaceHunter.Infrastructure.Persistence;
using RaceHunter.Worker.Endpoints;
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
var geminiProject = builder.Configuration["Gemini:ProjectId"];
if (!string.IsNullOrWhiteSpace(geminiProject))
{
    builder.Services.AddSingleton<IStructuredModelClient>(_ => new GoogleGenAiModelClient(
        geminiProject,
        builder.Configuration["Gemini:Location"] ?? "global"));
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IStructuredModelClient, DevelopmentModelClient>();
}
else
{
    throw new InvalidOperationException("Gemini:ProjectId is required outside Development; the deterministic fake cannot run production work.");
}
builder.Services.AddScoped<IScenarioPlanner, ScenarioPlanner>();
builder.Services.AddScoped<IExperimentStrategist, ExperimentStrategist>();
var pubSubProject = builder.Configuration["PubSub:ProjectId"];
if (!string.IsNullOrWhiteSpace(pubSubProject))
{
    builder.Services.AddPubSubWorkPublisher(
        pubSubProject,
        builder.Configuration["PubSub:TopicId"] ?? "racehunter-work",
        builder.Configuration["PubSub:DeadLetterTopicId"] ?? "racehunter-dead-letter",
        builder.Configuration.GetValue("PubSub:UseEmulator", false));
}
else
{
    builder.Services.AddSingleton<IWorkPublisher, UnavailableWorkPublisher>();
}
builder.Services.AddScoped<IPlanWorkHandler, PlanWorkHandler>();
builder.Services.AddScoped<ReferenceCampaignAttemptExecutor>();
builder.Services.AddScoped<ICampaignWorkHandler, CampaignRunner>();
builder.Services.AddScoped<IReplayExecution, ReferenceReplayExecution>();
builder.Services.AddScoped<WorkDispatcher>();
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
app.MapPubSubPushEndpoint();
app.MapReplayEndpoint();
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
