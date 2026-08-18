using System.Net.Http.Json;
using RaceHunter.Application.Replays;
using RaceHunter.Contracts;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Api.Replay;

internal sealed class WorkerReplayExecution(HttpClient client) : IReplayExecution
{
    public async Task<ReplayAttempt> ExecuteAsync(
        ReplayArtifact artifact,
        ReplayTargetMode targetMode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        idempotencyKey = VerifyFix.NormalizeIdempotencyKey(idempotencyKey);
        artifact.VerifyIntegrity();
        using var response = await client.PostAsJsonAsync("/internal/replays", new WorkerReplayRequest(
            artifact.Id,
            artifact.FindingId,
            artifact.ScenarioVersionId,
            artifact.InvariantVersionId,
            artifact.TargetSnapshot,
            artifact.Strategy,
            artifact.Seed,
            artifact.Steps.Select(item => new ReplayStepResponse(item.ActorId, item.StepId, item.OperationId, item.OffsetMilliseconds)).ToArray(),
            artifact.RequestTemplateJson,
            artifact.CreatedAtUtc,
            artifact.Fingerprint,
            targetMode.ToString(),
            idempotencyKey), cancellationToken);
        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync<WorkerReplayResponse>(cancellationToken)
            ?? throw new HttpRequestException("The worker returned no replay evidence.");
        artifact.VerifyIntegrity();
        if (item.ArtifactId != artifact.Id ||
            !string.Equals(item.TargetMode, targetMode.ToString(), StringComparison.Ordinal) ||
            !string.Equals(item.IdempotencyKey, idempotencyKey, StringComparison.Ordinal) ||
            !string.Equals(item.ArtifactFingerprint, artifact.Fingerprint, StringComparison.Ordinal))
            throw new HttpRequestException("The worker returned replay evidence for a different immutable request.");
        return ReplayAttempt.Complete(
            item.Id,
            item.ArtifactId,
            Enum.Parse<ReplayTargetMode>(item.TargetMode),
            Enum.Parse<InvariantOutcome>(item.Outcome),
            item.TraceReferences,
            item.ArtifactFingerprint,
            item.IdempotencyKey,
            item.CompletedAtUtc);
    }
}
