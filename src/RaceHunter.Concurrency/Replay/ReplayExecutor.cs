using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Concurrency.Replay;

public sealed class ReplayCandidate
{
    public ReplayCandidate(string strategy, int seed, IEnumerable<ReplayStep> steps)
    {
        Strategy = string.IsNullOrWhiteSpace(strategy) ? throw new ArgumentException("A strategy is required.") : strategy.Trim();
        Seed = seed;
        Steps = steps.OrderBy(item => item.ActorId).ThenBy(item => item.OffsetMilliseconds).ThenBy(item => item.StepId, StringComparer.Ordinal).ToArray();
        if (Steps.Count == 0) throw new ArgumentException("At least one replay step is required.", nameof(steps));
    }

    public string Strategy { get; }
    public int Seed { get; }
    public IReadOnlyList<ReplayStep> Steps { get; }
    public int ActorCount => Steps.Select(item => item.ActorId).Distinct().Count();
}

public sealed record ReplayObservation(InvariantOutcome Outcome, IReadOnlyList<string> TraceReferences);

public interface IReplayProbe
{
    Task<ReplayObservation> ExecuteAsync(ReplayCandidate candidate, ReplayTargetMode mode, CancellationToken cancellationToken);
}

public sealed class ReplayExecutor(Func<Guid>? idFactory = null)
{
    private readonly Func<Guid> idFactory = idFactory ?? Guid.NewGuid;

    public async Task<ReplayAttempt> ExecuteAsync(ReplayArtifact artifact, ReplayTargetMode mode, IReplayProbe probe, CancellationToken cancellationToken)
    {
        artifact.VerifyIntegrity();
        var fingerprint = artifact.Fingerprint;
        var candidate = new ReplayCandidate(artifact.Strategy, artifact.Seed, artifact.Steps);
        var observation = await probe.ExecuteAsync(candidate, mode, cancellationToken);
        artifact.VerifyIntegrity();
        return ReplayAttempt.Complete(idFactory(), artifact.Id, mode, observation.Outcome, observation.TraceReferences,
            fingerprint, $"replay:{idFactory().ToString("N")}", DateTime.UtcNow);
    }
}

public sealed record CausalTrace(long Sequence, int ActorId, string StepId, string Kind, string RequestId, DateTime OccurredAtUtc);
public sealed record ActorLane(int ActorId, IReadOnlyList<CausalTrace> Events);

public sealed class CausalTimelineProjector
{
    public IReadOnlyList<ActorLane> Project(IEnumerable<CausalTrace> traces) => traces
        .OrderBy(item => item.OccurredAtUtc)
        .ThenBy(item => item.Sequence)
        .GroupBy(item => item.ActorId)
        .OrderBy(group => group.Key)
        .Select(group => new ActorLane(group.Key, group.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Sequence).ToArray()))
        .ToArray();
}
