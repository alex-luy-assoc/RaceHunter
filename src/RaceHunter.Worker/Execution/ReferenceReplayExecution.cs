using RaceHunter.Application.Replays;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Worker.Execution;

internal sealed class ReferenceReplayExecution(
    ConcurrencyScheduler scheduler,
    ReferenceInventoryTargetClient target,
    IConfiguration configuration) : IReplayExecution
{
    public async Task<ReplayAttempt> ExecuteAsync(
        ReplayArtifact artifact,
        ReplayTargetMode targetMode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        artifact.VerifyIntegrity();
        var probe = new ReferenceReplayProbe(
            scheduler,
            target,
            CreateExecutionScope(artifact.Id, idempotencyKey),
            configuration["ReferenceTarget:DemoControlKey"]
                ?? throw new InvalidOperationException("ReferenceTarget:DemoControlKey is required for replay."));
        var observation = await probe.ExecuteAsync(
            new ReplayCandidate(artifact.Strategy, artifact.Seed, artifact.Steps),
            targetMode,
            cancellationToken);
        artifact.VerifyIntegrity();
        return ReplayAttempt.Complete(
            Guid.NewGuid(),
            artifact.Id,
            targetMode,
            observation.Outcome,
            observation.TraceReferences,
            artifact.Fingerprint,
            idempotencyKey,
            DateTime.UtcNow);
    }

    internal static string CreateExecutionScope(Guid artifactId, string idempotencyKey) => $"{artifactId:N}:{idempotencyKey}";

    private sealed class ReferenceReplayProbe(
        ConcurrencyScheduler scheduler,
        ReferenceInventoryTargetClient target,
        string executionKey,
        string demoControlKey) : IReplayProbe
    {
        public async Task<ReplayObservation> ExecuteAsync(
            ReplayCandidate candidate,
            ReplayTargetMode mode,
            CancellationToken cancellationToken)
        {
            await target.ResetAsync(mode, demoControlKey, $"verify:{executionKey}:reset", cancellationToken);
            var plan = new SchedulePlan(
                ParseKind(candidate.Strategy),
                candidate.Seed,
                candidate.Steps.Select((step, index) => new ScheduledActor(
                    step.ActorId,
                    TimeSpan.FromMilliseconds(step.OffsetMilliseconds),
                    candidate.Strategy == "checkpoint-interleaving" ? index + 1 : null,
                    $"{index}:{step.StepId}:{step.OperationId}")).ToArray());
            var budget = new ExperimentBudget(
                candidate.ActorCount,
                candidate.ActorCount,
                candidate.Steps.Count,
                0,
                TimeSpan.FromSeconds(30),
                0);
            var replayRunId = Guid.NewGuid();
            var result = await scheduler.ExecuteAsync(
                plan,
                budget,
                (actor, token) => target.PlaceOrderAsync(replayRunId, actor, $"verify:{executionKey}", token),
                cancellationToken);
            var invariant = new NumericBoundaryEvaluator().Evaluate(
                new NumericBoundaryInvariant("successful-orders", 1),
                result.Executions.SelectMany(item => item.TargetResult.Observations).ToArray());
            return new ReplayObservation(invariant.Outcome, invariant.TraceReferences);
        }

        private static ScheduleKind ParseKind(string strategy) => strategy switch
        {
            "simultaneous-start" => ScheduleKind.SimultaneousStart,
            "seeded-jitter" => ScheduleKind.SeededJitter,
            "checkpoint-interleaving" => ScheduleKind.CheckpointInterleaving,
            _ => throw new InvalidOperationException("The replay strategy is not allowlisted.")
        };
    }
}
