using RaceHunter.Api.Endpoints;
using RaceHunter.Api.Messaging;
using RaceHunter.Api.Replay;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Application.Projects;
using RaceHunter.Application.Replays;
using RaceHunter.Application.Runs;
using RaceHunter.Contracts;
using RaceHunter.Infrastructure.Messaging;
using RaceHunter.Infrastructure.Observability;
using RaceHunter.Infrastructure.Persistence;
using RaceHunter.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
var connectionString = builder.Configuration.GetConnectionString("RaceHunter")
    ?? throw new InvalidOperationException("ConnectionStrings:RaceHunter is required.");
builder.Services.AddRaceHunterPersistence(connectionString);
builder.Services.AddRaceHunterTelemetry(builder.Configuration, "racehunter-api");
builder.Services.AddSingleton<IDnsResolver, SystemDnsResolver>();
builder.Services.AddSingleton<IManualTargetSafetyPolicy>(provider => new TargetDestinationValidator(
    provider.GetRequiredService<IDnsResolver>(),
    builder.Environment.IsDevelopment(),
    builder.Configuration.GetSection("ManualTargets:DevelopmentHosts").Get<string[]>() ?? []));
builder.Services.AddScoped<ConfigureManualTarget>();
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
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxDispatchService>();
builder.Services.AddHttpClient<IIdentityTokenSource, MetadataIdentityTokenSource>(client => client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false });
var replayClient = builder.Services.AddHttpClient<IReplayExecution, WorkerReplayExecution>((services, client) =>
{
    var baseUrl = services.GetRequiredService<IConfiguration>()["Worker:BaseUrl"]
        ?? throw new InvalidOperationException("Worker:BaseUrl is required for Verify Fix replay execution.");
    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(30);
});
if (builder.Configuration.GetValue("Worker:RequireAuthentication", false))
{
    builder.Services.AddTransient(provider => new CloudRunIdentityTokenHandler(
        builder.Configuration["Worker:Audience"] ?? throw new InvalidOperationException("Worker:Audience is required when worker authentication is enabled."),
        provider.GetRequiredService<IIdentityTokenSource>()));
    replayClient.AddHttpMessageHandler<CloudRunIdentityTokenHandler>();
}
builder.Services.AddHealthChecks();

var app = builder.Build();
await app.Services.ApplyRaceHunterMigrationsAsync();
app.UseRaceHunterRequestTelemetry();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/healthz");
app.MapPost("/api/projects", async (CreateProjectRequest request, ProjectService service, CancellationToken cancellationToken) =>
{
    var project = await service.CreateAsync(request.Name, cancellationToken);
    return Results.Created($"/api/projects/{project.Id}", new ProjectResponse(project.Id, project.Name, project.CreatedAtUtc));
});
app.MapGet("/api/projects/{id:guid}", async (Guid id, ProjectService service, CancellationToken cancellationToken) =>
{
    var project = await service.GetAsync(id, cancellationToken);
    return project is null
        ? Results.NotFound()
        : Results.Ok(new ProjectResponse(project.Id, project.Name, project.CreatedAtUtc));
});
app.MapRunEndpoints();
app.MapHuntEndpoints();
app.MapManualTargetEndpoints();
app.MapGet("/api/capabilities", () => Results.Ok(new { manualTargetsEnabled = app.Environment.IsDevelopment() }));
app.MapGet("/api/cloud-proof", async (Guid runId, GetCloudExecutionEvidence query, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var evidence = await query.ExecuteAsync(runId, cancellationToken);
    if (evidence is null) return Results.NotFound();
    return Results.Ok(new CloudProofResponse(
        Environment.GetEnvironmentVariable("K_REVISION") ?? "docker-compose-local",
        configuration["CloudProof:WorkerService"] ?? "racehunter-worker-local",
        configuration["PubSub:TopicId"] ?? "racehunter-work",
        configuration["CloudProof:CloudSqlInstance"] ?? "postgres-local",
        evidence.ModelId,
        evidence.SchemaVersion,
        configuration.GetValue("Worker:RequireAuthentication", false) ? "OIDC ID token" : "development network",
        evidence.RunId,
        evidence.RunStatus,
        evidence.PlanVersion,
        evidence.WorkerExecution,
        evidence.ModelInvocationId,
        evidence.TraceEventCount,
        evidence.FindingId,
        evidence.EvidenceCorrelationId,
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty));
});
app.MapFindingEndpoints();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
