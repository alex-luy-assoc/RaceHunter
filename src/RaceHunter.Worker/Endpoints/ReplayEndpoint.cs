using RaceHunter.Application.Replays;
using RaceHunter.Contracts;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Worker.Endpoints;

internal static class ReplayEndpoint
{
    internal static IEndpointRouteBuilder MapReplayEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/internal/replays", async (
            WorkerReplayRequest request,
            IReplayExecution execution,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var idempotencyKey = VerifyFix.NormalizeIdempotencyKey(request.IdempotencyKey);
                var artifact = ReplayArtifact.Rehydrate(
                    request.ArtifactId,
                    request.FindingId,
                    request.ScenarioVersionId,
                    request.InvariantVersionId,
                    request.TargetSnapshot,
                    request.Strategy,
                    request.Seed,
                    request.Steps.Select(item => new ReplayStep(item.ActorId, item.StepId, item.OperationId, item.OffsetMilliseconds)).ToArray(),
                    request.RequestTemplateJson,
                    request.CreatedAtUtc,
                    request.Fingerprint);
                var mode = Enum.Parse<ReplayTargetMode>(request.TargetMode);
                var result = await execution.ExecuteAsync(artifact, mode, idempotencyKey, cancellationToken);
                return Results.Ok(new WorkerReplayResponse(
                    result.Id,
                    result.ArtifactId,
                    result.TargetMode.ToString(),
                    result.Outcome.ToString(),
                    result.TraceReferences,
                    result.ArtifactFingerprint,
                    result.IdempotencyKey,
                    result.CompletedAtUtc));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid replay artifact", detail: exception.Message);
            }
        });
        return endpoints;
    }
}
