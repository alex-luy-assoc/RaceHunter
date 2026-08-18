using System.Text.Json;
using RaceHunter.Application.Hunts;
using RaceHunter.Contracts;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Common;

namespace RaceHunter.Api.Endpoints;

internal static class HuntEndpoints
{
    internal static IEndpointRouteBuilder MapHuntEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/hunts", CreateAsync);
        endpoints.MapPost("/api/hunts/{id:guid}/plan", RequestPlanAsync);
        endpoints.MapGet("/api/hunts/{id:guid}/plan", GetPlanAsync);
        endpoints.MapPost("/api/hunts/{id:guid}/runs", ApproveAsync);
        endpoints.MapGet("/api/hunts/{id:guid}/events", GetEventsAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateHuntRequest request, CreateHunt command, CancellationToken cancellationToken)
    {
        try
        {
            var hunt = await command.ExecuteAsync(request.Objective, new ExperimentBudget(
                request.MaxActors,
                request.MaxConcurrency,
                request.MaxRequests,
                request.MaxModelCalls,
                TimeSpan.FromSeconds(request.MaxDurationSeconds),
                request.MaxRetries), cancellationToken);
            return Results.Created($"/api/hunts/{hunt.Id}", new HuntResponse(hunt.Id, hunt.Objective, hunt.Status.ToString(), hunt.CreatedAtUtc));
        }
        catch (DomainException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid hunt", detail: exception.Message);
        }
    }

    private static async Task<IResult> RequestPlanAsync(Guid id, GeneratePlan command, CancellationToken cancellationToken)
    {
        try
        {
            await command.ExecuteAsync(id, cancellationToken);
            return Results.Accepted($"/api/hunts/{id}/plan");
        }
        catch (DomainException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Planning request rejected", detail: exception.Message);
        }
    }

    private static async Task<IResult> GetPlanAsync(Guid id, IHuntStore store, CancellationToken cancellationToken)
    {
        var hunt = await store.GetAsync(id, cancellationToken);
        if (hunt is null) return Results.NotFound();
        if (hunt.Status == HuntStatus.PlanningFailed)
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: hunt.FailureOutcome?.StartsWith("DeadLettered", StringComparison.Ordinal) == true ? "Planning work dead-lettered" : "Model planning failed",
                detail: hunt.FailureDiagnostic ?? "The schema-constrained plan could not be validated.");
        if (hunt.Plan is null) return Results.Accepted($"/api/hunts/{id}/plan");
        var plan = hunt.Plan;
        return Results.Ok(new PlanResponse(
            plan.PlanVersion,
            plan.SchemaVersion,
            plan.PromptVersion,
            plan.ModelId,
            plan.Actors.Select(item => new PlanActorResponse(item.Name, item.OperationId)).ToArray(),
            new PlanInvariantResponse(
                plan.Invariant.Type,
                plan.Invariant.Metric,
                plan.Invariant.Maximum,
                plan.Invariant.LeftMetric,
                plan.Invariant.RightMetric,
                plan.Invariant.Relation),
            new PlanStrategyResponse(plan.Strategy.Kind, plan.Strategy.ActorCount, plan.Strategy.Seed)));
    }

    private static async Task<IResult> ApproveAsync(Guid id, ApproveRunRequest request, ApproveAndRun command, CancellationToken cancellationToken)
    {
        try
        {
            var approval = await command.ExecuteAsync(id, request.PlanVersion, request.IdempotencyKey, cancellationToken);
            return Results.Accepted($"/api/runs/{approval.RunId}", new ApprovalResponse(approval.RunId, approval.PlanVersion));
        }
        catch (DomainException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Plan approval rejected", detail: exception.Message);
        }
    }

    private static async Task<IResult> GetEventsAsync(Guid id, long? after, HttpContext context, IHuntStore store, CancellationToken cancellationToken)
    {
        var hunt = await store.GetAsync(id, cancellationToken);
        if (hunt is null) return Results.NotFound();
        var cursor = Math.Max(0, after ?? 0);
        var acceptsSse = context.Request.GetTypedHeaders().Accept?.Any(item => item.MediaType.Value == "text/event-stream") == true;
        if (!acceptsSse) return Results.Ok(await store.GetEventsAsync(id, cursor, cancellationToken));

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        while (!cancellationToken.IsCancellationRequested)
        {
            var events = await store.GetEventsAsync(id, cursor, cancellationToken);
            foreach (var item in events)
            {
                var data = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await context.Response.WriteAsync($"id: {item.Cursor}\nevent: {item.Kind}\ndata: {data}\n\n", cancellationToken);
                cursor = item.Cursor;
            }
            if (events.Count > 0) await context.Response.Body.FlushAsync(cancellationToken);
            hunt = await store.GetAsync(id, cancellationToken);
            if ((hunt?.Status is HuntStatus.AwaitingApproval or HuntStatus.PlanningFailed) && events.Count == 0) break;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        return Results.Empty;
    }
}
