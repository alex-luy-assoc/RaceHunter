using RaceHunter.Application.Findings;
using RaceHunter.Application.Replays;
using RaceHunter.Contracts;

namespace RaceHunter.Api.Endpoints;

internal static class FindingEndpoints
{
    internal static IEndpointRouteBuilder MapFindingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/findings/{id:guid}", async (Guid id, GetFinding query, CancellationToken cancellationToken) =>
        {
            var finding = await query.ExecuteAsync(id, cancellationToken);
            return finding is null ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Finding not found") : Results.Ok(ToResponse(finding));
        });
        endpoints.MapPost("/api/findings/{id:guid}/replays", async (
            Guid id,
            VerifyFixRequest request,
            VerifyFix command,
            GetFinding query,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var attempt = await command.ExecuteAsync(id, request.IdempotencyKey, cancellationToken);
                var finding = await query.ExecuteAsync(id, cancellationToken)
                    ?? throw new KeyNotFoundException("The finding does not exist.");
                var vulnerable = finding.ReplayAttempts.LastOrDefault(item => item.TargetMode == "Vulnerable")?.Outcome
                    ?? finding.InvariantOutcome.ToString();
                return Results.Accepted($"/api/findings/{id}", new ReplayComparisonResponse(
                    vulnerable,
                    attempt.Outcome.ToString(),
                    attempt.ArtifactFingerprint,
                    attempt.IdempotencyKey));
            }
            catch (KeyNotFoundException)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Finding not found");
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid replay request", detail: exception.Message);
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Verify Fix unavailable", detail: "Fixed-target replay is temporarily unavailable.");
            }
        });
        return endpoints;
    }

    private static FindingResponse ToResponse(FindingProjection item) => new(
        item.Id,
        item.RunId,
        item.SuccessMessage,
        item.InvariantOutcome.ToString(),
        item.InvariantSummary,
        item.TraceReferences,
        item.AgentInterpretation,
        item.Reproductions.Select(value => new ReproductionResponse(value.Attempt, value.Outcome.ToString(), value.TraceReferences)).ToArray(),
        new ReplayArtifactResponse(
            item.ReplayArtifact.Id,
            item.ReplayArtifact.Fingerprint,
            item.ReplayArtifact.Strategy,
            item.ReplayArtifact.Seed,
            item.ReplayArtifact.ActorCount,
            item.ReplayArtifact.StepCount,
            item.ReplayArtifact.Steps.Select(value => new ReplayStepResponse(value.ActorId, value.StepId, value.OperationId, value.OffsetMilliseconds)).ToArray()),
        item.Timeline.Select(lane => new ActorLaneResponse(
            lane.ActorId,
            lane.Events.Select(value => new TimelineEventResponse(value.Sequence, value.AttemptId, value.StepId, value.Kind, value.RequestId, value.OccurredAtUtc)).ToArray())).ToArray(),
        item.AgentActivity.Select(value => new AgentActivityResponse(
            value.Iteration,
            value.Action,
            value.RationaleSummary,
            value.ModelId,
            value.SchemaVersion,
            value.ModelInvocationId,
            value.OccurredAtUtc)).ToArray(),
        item.ReplayAttempts.Select(value => new ReplayAttemptResponse(
            value.Id,
            value.TargetMode,
            value.Outcome,
            value.ArtifactFingerprint,
            value.IdempotencyKey,
            value.CompletedAtUtc)).ToArray());
}
