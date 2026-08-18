using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Agents;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Runs;
using RaceHunter.Domain.Tracing;

namespace RaceHunter.Worker.Execution;

internal sealed class ReferenceCampaignAttemptExecutor(
    ConcurrencyScheduler scheduler,
    ReferenceInventoryTargetClient target,
    ITraceStore traceStore,
    IRunAttemptStore attemptStore)
{
    public Task<DeterministicAttemptResult> ExecuteAsync(
        Guid runId,
        ExperimentBudget campaignBudget,
        InvariantDefinition invariant,
        int seed,
        CampaignSettings settings,
        CancellationToken cancellationToken)
    {
        var plan = CreatePlan(settings, seed);
        return ExecutePlanAsync(runId, campaignBudget, invariant, plan, cancellationToken);
    }

    public Task<DeterministicAttemptResult> ExecuteReplayAsync(
        Guid runId,
        ExperimentBudget campaignBudget,
        InvariantDefinition invariant,
        ReplayCandidate candidate,
        CancellationToken cancellationToken)
    {
        var plan = new SchedulePlan(
            ParseKind(candidate.Strategy),
            candidate.Seed,
            candidate.Steps.Select((step, index) => new ScheduledActor(
                step.ActorId,
                TimeSpan.FromMilliseconds(step.OffsetMilliseconds),
                candidate.Strategy == "checkpoint-interleaving" ? index + 1 : null)).ToArray());
        return ExecutePlanAsync(runId, campaignBudget, invariant, plan, cancellationToken);
    }

    private async Task<DeterministicAttemptResult> ExecutePlanAsync(
        Guid runId,
        ExperimentBudget campaignBudget,
        InvariantDefinition invariant,
        SchedulePlan plan,
        CancellationToken cancellationToken)
    {
        var attempt = RunAttempt.Start(Guid.NewGuid(), runId, plan.Kind.ToString(), plan.Seed, DateTime.UtcNow);
        await attemptStore.AddAsync(attempt, cancellationToken);
        var previous = await traceStore.GetAsync(runId, 0, cancellationToken);
        var nextSequence = previous.Count == 0 ? 0L : previous.Max(item => item.Sequence);
        var persisted = new List<TraceEvent>();
        var persistenceGate = new SemaphoreSlim(1, 1);
        var attemptBudget = new ExperimentBudget(
            plan.Actors.Count,
            Math.Min(plan.Actors.Count, campaignBudget.MaxConcurrentActors),
            plan.Actors.Count,
            campaignBudget.MaxModelCalls,
            campaignBudget.MaxDuration,
            campaignBudget.MaxRetries);

        try
        {
            var result = await scheduler.ExecuteAsync(plan, attemptBudget, async (actor, token) =>
            {
                var targetResult = await target.PlaceOrderAsync(runId, actor, token);
                await persistenceGate.WaitAsync(CancellationToken.None);
                try
                {
                    var trace = new TraceEvent(
                        ++nextSequence,
                        runId,
                        attempt.Id,
                        actor.ActorId,
                        "target-operation",
                        targetResult.Succeeded ? "response-success" : "response-failure",
                        targetResult.RequestId ?? $"actor-{actor.ActorId}",
                        DateTime.UtcNow);
                    await traceStore.AppendAsync(trace, CancellationToken.None);
                    persisted.Add(trace);
                }
                finally
                {
                    persistenceGate.Release();
                }
                return targetResult;
            }, cancellationToken);
            if (result.Cancelled)
            {
                attempt.Cancel(DateTime.UtcNow);
                await attemptStore.SaveAsync(attempt, CancellationToken.None);
                throw new OperationCanceledException(cancellationToken);
            }
            var observations = result.Executions.SelectMany(item => item.TargetResult.Observations).ToArray();
            var invariantResult = new InvariantEvaluatorRegistry().Evaluate(invariant, observations);
            attempt.Complete(DateTime.UtcNow);
            await attemptStore.SaveAsync(attempt, CancellationToken.None);
            return new DeterministicAttemptResult(
                invariantResult.Outcome,
                persisted.Select(item => $"trace:{item.Sequence}").ToArray(),
                result.Executions.Count,
                plan.Actors.Select(item => new DeterministicReplayStep(
                    item.ActorId,
                    checked((int)item.Offset.TotalMilliseconds))).ToArray());
        }
        catch
        {
            if (attempt.Status == RunAttemptStatus.Running)
            {
                attempt.Fail(DateTime.UtcNow);
                await attemptStore.SaveAsync(attempt, CancellationToken.None);
            }
            throw;
        }
    }

    private static SchedulePlan CreatePlan(CampaignSettings settings, int seed) => settings.Strategy switch
    {
        "simultaneous-start" => new SimultaneousStartStrategy().Create(settings.ActorCount, seed),
        "seeded-jitter" => new SeededJitterStrategy(TimeSpan.FromMilliseconds(Math.Max(1, settings.TimingAdjustmentMs))).Create(settings.ActorCount, seed),
        "checkpoint-interleaving" => new CheckpointStrategy().Create(settings.ActorCount, seed),
        _ => throw new InvalidOperationException("The persisted strategy was not allowlisted.")
    };

    private static ScheduleKind ParseKind(string strategy) => strategy switch
    {
        "simultaneous-start" => ScheduleKind.SimultaneousStart,
        "seeded-jitter" => ScheduleKind.SeededJitter,
        "checkpoint-interleaving" => ScheduleKind.CheckpointInterleaving,
        _ => throw new InvalidOperationException("The replay strategy was not allowlisted.")
    };
}
