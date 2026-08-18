using RaceHunter.Api.Security;
using RaceHunter.Api.Streaming;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Runs;
using RaceHunter.Contracts;
using RaceHunter.Domain.Runs;
using RaceHunter.Infrastructure.Observability;

namespace RaceHunter.Api.Endpoints;

internal static class RunEndpoints
{
    internal static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/runs/{id:guid}", async (Guid id, HttpContext context, GetRun query, IHuntStore hunts,
            IManualTargetStore targets, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var denied = await ManualResourceAuthorization.ForRunAsync(context, id, hunts, targets, configuration, cancellationToken);
            if (denied is not null) return denied;
            var run = await query.ExecuteAsync(id, cancellationToken);
            return run is null ? Results.NotFound() : Results.Ok(new RunResponse(
                run.Id,
                run.Status.ToString(),
                run.Budget.MaxActors,
                run.Budget.MaxConcurrentActors,
                run.Budget.MaxRequests,
                run.Budget.MaxModelCalls,
                checked((int)run.Budget.MaxDuration.TotalSeconds),
                run.CreatedAtUtc,
                run.StartedAtUtc,
                run.CompletedAtUtc,
                run.CancellationRequestedAtUtc,
                await query.GetFindingIdAsync(id, cancellationToken)));
        });
        endpoints.MapGet("/api/runs/{id:guid}/events", GetEventsAsync);
        endpoints.MapGet("/api/runs/{id:guid}/traces", async (Guid id, long? after, HttpContext context, GetRun query,
            IHuntStore hunts, IManualTargetStore targets, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var denied = await ManualResourceAuthorization.ForRunAsync(context, id, hunts, targets, configuration, cancellationToken);
            if (denied is not null) return denied;
            if (await query.ExecuteAsync(id, cancellationToken) is null) return Results.NotFound();
            return Results.Ok((await query.GetTracesAsync(id, after ?? 0, cancellationToken))
                .Select(item => new TraceEventResponse(item.Sequence, item.AttemptId, item.ActorId, item.StepId, item.Kind, item.RequestId, item.OccurredAtUtc)));
        });
        endpoints.MapPost("/api/runs/{id:guid}/cancel", async (Guid id, HttpContext context, CancelRun command,
            IHuntStore hunts, IManualTargetStore targets, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var denied = await ManualResourceAuthorization.ForRunAsync(context, id, hunts, targets, configuration, cancellationToken);
            if (denied is not null) return denied;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            using var activity = RaceHunterTelemetry.Activities.StartActivity("racehunter.run.cancel");
            activity?.SetTag("racehunter.run.id", id.ToString());
            var run = await command.ExecuteAsync(id, cancellationToken);
            RaceHunterTelemetry.CancellationLatency.Record(
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("outcome", run is null ? "not-found" : "persisted"));
            return run is null ? Results.NotFound() : Results.Accepted($"/api/runs/{id}");
        });
        return endpoints;
    }

    private static async Task<IResult> GetEventsAsync(Guid id, long? after, HttpContext httpContext, GetRun query,
        IHuntStore hunts, IManualTargetStore targets, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var denied = await ManualResourceAuthorization.ForRunAsync(httpContext, id, hunts, targets, configuration, cancellationToken);
        if (denied is not null) return denied;
        var run = await query.ExecuteAsync(id, cancellationToken);
        if (run is null) return Results.NotFound();
        var acceptsSse = httpContext.Request.GetTypedHeaders().Accept?.Any(item => item.MediaType.Value == "text/event-stream") == true;
        if (!acceptsSse)
        {
            return Results.Ok((await query.GetEventsAsync(id, after ?? 0, cancellationToken))
                .Select(item => new RunEventResponse(item.Cursor, item.Kind, item.Message, item.OccurredAtUtc)));
        }

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";
        var cursor = RunEventSse.ResolveAfter(after, httpContext.Request.Headers["Last-Event-ID"].ToString());
        while (!cancellationToken.IsCancellationRequested)
        {
            var events = await query.GetEventsAsync(id, cursor, cancellationToken);
            foreach (var item in events)
            {
                await httpContext.Response.WriteAsync(RunEventSse.Format(item), cancellationToken);
                cursor = item.Cursor;
            }
            if (events.Count > 0) await httpContext.Response.Body.FlushAsync(cancellationToken);
            run = await query.ExecuteAsync(id, cancellationToken);
            if (run?.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled && events.Count == 0) break;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        return Results.Empty;
    }
}
