using System.Text.Json;
using System.Text.Json.Nodes;
using RaceHunter.Application.Abstractions;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Worker.Execution;

internal sealed class DurableFindingReplayProbe(
    Guid runId,
    int remainingRequests,
    IFindingProbeCheckpointStore checkpoints,
    Func<string, ReplayCandidate, ReplayTargetMode, CancellationToken, Task<int>> measureUniqueWork,
    Func<string, ReplayCandidate, ReplayTargetMode, CancellationToken, Task<ReplayObservation>> executePhysical) : IKeyedReplayProbe
{
    private int remaining = remainingRequests;

    public Task<ReplayObservation> ExecuteAsync(ReplayCandidate candidate, ReplayTargetMode mode, CancellationToken cancellationToken) =>
        ExecuteAsync($"adhoc:{Guid.NewGuid():N}", candidate, mode, cancellationToken);

    public async Task<ReplayObservation> ExecuteAsync(
        string probeKey,
        ReplayCandidate candidate,
        ReplayTargetMode mode,
        CancellationToken cancellationToken)
    {
        var candidateJson = CandidateJson(candidate);
        var existing = await checkpoints.GetAsync(runId, probeKey, cancellationToken);
        if (existing is not null)
        {
            if (!JsonNode.DeepEquals(JsonNode.Parse(existing.CandidateJson), JsonNode.Parse(candidateJson)))
                throw new InvalidOperationException("A persisted finding probe key was reused for a different replay candidate.");
            return new ReplayObservation(Enum.Parse<InvariantOutcome>(existing.Outcome), existing.TraceReferences);
        }

        var required = await measureUniqueWork(probeKey, candidate, mode, cancellationToken);
        if (required > remaining) return new ReplayObservation(InvariantOutcome.Inconclusive, []);
        var observation = await executePhysical(probeKey, candidate, mode, cancellationToken);
        var consumed = observation.RequestsConsumed < 0 ? candidate.Steps.Count : observation.RequestsConsumed;
        if (consumed > remaining)
            throw new InvalidOperationException("Physical work exceeded the durable request budget after recovery accounting.");
        remaining -= consumed;
        var parts = probeKey.Split(':');
        await checkpoints.SaveAsync(new FindingProbeCheckpoint(
            runId,
            probeKey,
            parts[0],
            ParseOrdinal(parts),
            candidateJson,
            observation.Outcome.ToString(),
            observation.TraceReferences,
            consumed,
            DateTime.UtcNow), cancellationToken);
        return observation;
    }

    internal static string CandidateJson(ReplayCandidate candidate) => JsonSerializer.Serialize(new
    {
        candidate.Strategy,
        candidate.Seed,
        Steps = candidate.Steps.Select(item => new { item.ActorId, item.StepId, item.OperationId, item.OffsetMilliseconds })
    });

    private static int ParseOrdinal(IReadOnlyList<string> parts)
    {
        var value = parts.Count > 1 && parts[0] == "reproduction" ? parts[1]
            : parts.Count > 3 && parts[0] == "minimize" && parts[1] == "step" ? parts[3]
            : parts.Count > 2 && parts[0] == "minimize" ? parts[2]
            : "0";
        return int.TryParse(value, out var ordinal) ? ordinal : 0;
    }
}
