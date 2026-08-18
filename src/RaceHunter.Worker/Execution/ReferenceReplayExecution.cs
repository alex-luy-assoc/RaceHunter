using System.Text.Json;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Replays;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using RaceHunter.Infrastructure.Observability;

namespace RaceHunter.Worker.Execution;

internal sealed class ReferenceReplayExecution(
    ConcurrencyScheduler scheduler,
    ReferenceInventoryTargetClient target,
    ManualHttpTargetClient manualTarget,
    IConfiguration configuration) : IReplayExecution
{
    public async Task<ReplayAttempt> ExecuteAsync(
        ReplayArtifact artifact,
        ReplayTargetMode targetMode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var activity = RaceHunterTelemetry.Activities.StartActivity("racehunter.replay.execute");
        activity?.SetTag("racehunter.finding.id", artifact.FindingId.ToString());
        activity?.SetTag("racehunter.artifact.id", artifact.Id.ToString());
        activity?.SetTag("racehunter.replay.target_mode", targetMode.ToString());
        artifact.VerifyIntegrity();
        if (artifact.TargetSnapshot.StartsWith("{\"kind\":\"manual-http-json\"", StringComparison.Ordinal))
            return await ExecuteManualAsync(artifact, targetMode, idempotencyKey, cancellationToken);
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

    private async Task<ReplayAttempt> ExecuteManualAsync(ReplayArtifact artifact, ReplayTargetMode targetMode,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var snapshot = ManualReplaySnapshot.Deserialize(artifact.TargetSnapshot);
        var current = await manualTarget.GetSnapshotAsync(snapshot.TargetId, cancellationToken);
        EnsureSnapshotMatches(current, snapshot);
        var scope = CreateExecutionScope(artifact.Id, idempotencyKey);
        _ = await manualTarget.PrepareAsync(snapshot.TargetId, artifact.FindingId, scope, cancellationToken);
        var plan = new SchedulePlan(ParseKind(artifact.Strategy), artifact.Seed,
            artifact.Steps.Select((step, index) => new ScheduledActor(step.ActorId,
                TimeSpan.FromMilliseconds(step.OffsetMilliseconds),
                artifact.Strategy == "checkpoint-interleaving" ? index + 1 : null, step.OperationId)).ToArray());
        var operations = artifact.Steps.GroupBy(step => step.ActorId).ToDictionary(group => group.Key, group => group.First().OperationId);
        var result = await scheduler.ExecuteAsync(plan,
            new ExperimentBudget(artifact.ActorCount, artifact.ActorCount, artifact.Steps.Count, 0, TimeSpan.FromSeconds(30), 0),
            (actor, token) => manualTarget.ExecuteAsync(snapshot.TargetId, artifact.FindingId, actor,
                operations[actor.ActorId], scope, token), cancellationToken);
        var invariant = new InvariantEvaluatorRegistry().Evaluate(CompileManualInvariant(artifact.TargetSnapshot),
            result.Executions.SelectMany(execution => execution.TargetResult.Observations).ToArray());
        artifact.VerifyIntegrity();
        return ReplayAttempt.Complete(Guid.NewGuid(), artifact.Id, targetMode, invariant.Outcome,
            invariant.TraceReferences, artifact.Fingerprint, idempotencyKey, DateTime.UtcNow);
    }

    internal static void EnsureSnapshotMatches(RaceHunter.Application.Hunts.ManualTargetSnapshot current,
        ManualReplaySnapshot snapshot)
    {
        if (!string.Equals(current.BaseUri.AbsoluteUri, snapshot.BaseUrl, StringComparison.Ordinal) ||
            !string.Equals(current.Host, snapshot.Host, StringComparison.Ordinal) ||
            !string.Equals(current.CredentialReference, snapshot.CredentialReference, StringComparison.Ordinal) ||
            JsonSerializer.Serialize(current.Operations) != JsonSerializer.Serialize(snapshot.Operations) ||
            !current.SensitiveJsonPaths.Order(StringComparer.Ordinal).SequenceEqual(
                snapshot.SensitiveJsonPaths.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("The authorized manual target no longer matches the immutable replay snapshot.");
    }

    internal static InvariantDefinition CompileManualInvariant(string targetSnapshot) =>
        PlannedInvariantCompiler.Compile(ManualReplaySnapshot.Deserialize(targetSnapshot).Invariant);

    private static ScheduleKind ParseKind(string strategy) => strategy switch
    {
        "simultaneous-start" => ScheduleKind.SimultaneousStart,
        "seeded-jitter" => ScheduleKind.SeededJitter,
        "checkpoint-interleaving" => ScheduleKind.CheckpointInterleaving,
        _ => throw new InvalidOperationException("The replay strategy is not allowlisted.")
    };

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
