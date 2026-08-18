using RaceHunter.Api.Endpoints;
using RaceHunter.Api.Messaging;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Application.Projects;
using RaceHunter.Contracts;
using RaceHunter.Infrastructure.Messaging;
using RaceHunter.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("RaceHunter")
    ?? throw new InvalidOperationException("ConnectionStrings:RaceHunter is required.");
builder.Services.AddRaceHunterPersistence(connectionString);
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
builder.Services.AddHealthChecks();

var app = builder.Build();
await app.Services.ApplyRaceHunterMigrationsAsync();
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
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
