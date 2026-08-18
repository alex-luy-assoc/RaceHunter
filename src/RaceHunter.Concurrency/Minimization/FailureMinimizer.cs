using RaceHunter.Concurrency.Replay;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Concurrency.Minimization;

public enum ReductionKind
{
    Actor,
    Step
}

public sealed record ReductionDecision(ReductionKind Kind, string Removed, InvariantOutcome Outcome);
public sealed record MinimizationResult(ReplayCandidate Candidate, IReadOnlyList<ReductionDecision> AcceptedReductions);
public sealed record ReproductionResult(bool Verified, int Failures, IReadOnlyList<ReproductionAttemptResult> Attempts);
public sealed record ReproductionAttemptResult(int Attempt, InvariantOutcome Outcome, IReadOnlyList<string> TraceReferences);

public sealed class ReproductionVerifier
{
    public async Task<ReproductionResult> VerifyReferenceAsync(ReplayCandidate candidate, IReplayProbe probe, CancellationToken cancellationToken)
    {
        const int requiredAttempts = 3;
        var attempts = new List<ReproductionAttemptResult>(requiredAttempts);
        for (var attempt = 1; attempt <= requiredAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = await probe.ExecuteAsync(candidate, ReplayTargetMode.Vulnerable, cancellationToken);
            attempts.Add(new ReproductionAttemptResult(attempt, observation.Outcome, observation.TraceReferences.ToArray()));
        }
        var failures = attempts.Count(item => item.Outcome == InvariantOutcome.Fail);
        return new ReproductionResult(failures == requiredAttempts, failures, attempts);
    }
}

public sealed class FailureMinimizer
{
    public async Task<MinimizationResult> MinimizeAsync(ReplayCandidate original, IReplayProbe probe, CancellationToken cancellationToken)
    {
        var current = original;
        var accepted = new List<ReductionDecision>();
        foreach (var actorId in current.Steps.Select(item => item.ActorId).Distinct().OrderDescending().ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.ActorCount <= 2) break;
            var candidate = new ReplayCandidate(current.Strategy, current.Seed, current.Steps.Where(item => item.ActorId != actorId));
            var observation = await probe.ExecuteAsync(candidate, ReplayTargetMode.Vulnerable, cancellationToken);
            if (observation.Outcome != InvariantOutcome.Fail) continue;
            current = candidate;
            accepted.Add(new ReductionDecision(ReductionKind.Actor, $"actor:{actorId}", observation.Outcome));
        }

        foreach (var step in current.Steps.OrderByDescending(item => item.ActorId).ThenByDescending(item => item.OffsetMilliseconds).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Steps.Count(item => item.ActorId == step.ActorId) <= 1) continue;
            var selectedIndex = -1;
            for (var index = 0; index < current.Steps.Count; index++)
            {
                if (!ReferenceEquals(current.Steps[index], step)) continue;
                selectedIndex = index;
                break;
            }
            if (selectedIndex < 0) continue;
            var candidateSteps = current.Steps.Where((_, index) => index != selectedIndex).ToArray();
            var candidate = new ReplayCandidate(current.Strategy, current.Seed, candidateSteps);
            var observation = await probe.ExecuteAsync(candidate, ReplayTargetMode.Vulnerable, cancellationToken);
            if (observation.Outcome != InvariantOutcome.Fail) continue;
            current = candidate;
            accepted.Add(new ReductionDecision(ReductionKind.Step, $"actor:{step.ActorId}/step:{step.StepId}", observation.Outcome));
        }
        return new MinimizationResult(current, accepted);
    }
}
