using System.Text.Json;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Runs;

namespace RaceHunter.Worker.Execution;

internal sealed class CampaignRunner(
    IHuntStore hunts,
    IRunStore runs,
    IWorkInbox inbox,
    IAgentDecisionCheckpointStore decisionCheckpoints,
    IExperimentStrategist strategist,
    ReferenceCampaignAttemptExecutor attemptExecutor)
{
    public async Task ExecuteAsync(
        Guid runId,
        Guid workId,
        string leaseOwner,
        WorkCheckpoint? recoveredCheckpoint,
        CancellationToken cancellationToken)
    {
        var hunt = await hunts.GetByRunAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("The approved hunt for this run does not exist.");
        var plan = hunt.Plan ?? throw new InvalidOperationException("The approved plan is missing.");
        var run = await runs.GetAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("The queued run does not exist.");
        if (run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled) return;
        if (run.Status == RunStatus.Queued)
        {
            run.Start(DateTime.UtcNow);
            run.AppendEvent("campaign-started", $"Approved plan {plan.PlanVersion} started.", DateTime.UtcNow);
            await runs.SaveAsync(run, cancellationToken);
        }

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(run.Budget.MaxDuration);
        var recovered = RecoverSettings(recoveredCheckpoint, plan);
        var context = new AdaptiveCampaignContext(
            run.Id,
            recovered.Settings,
            ["simultaneous-start", "seeded-jitter", "checkpoint-interleaving"],
            run.Budget,
            Math.Max(1, Math.Min(run.Budget.MaxModelCalls, 5)),
            recovered.ResumeAfterIteration,
            recovered.RequestsConsumed,
            recovered.HasCheckpoint ? recovered.ModelCallsConsumed : plan.ModelCallsConsumed,
            recovered.RecoveredAttempt);
        var invariant = ToInvariant(plan.Invariant);
        try
        {
            var result = await new AdaptiveStrategyLoop(strategist).RunAsync(
                context,
                (settings, token) => attemptExecutor.ExecuteAsync(run.Id, run.Budget, invariant, plan.Strategy.Seed, settings, token),
                bounded.Token,
                async (iteration, decision, evidence, token) =>
                {
                    var iterationRecord = new AgentIterationRecord(
                        Guid.NewGuid(),
                        run.Id,
                        iteration,
                        "Sanitized deterministic attempt evidence",
                        decision.Action.ToString(),
                        decision.RationaleSummary,
                        decision.ModelId,
                        decision.SchemaVersion,
                        decision.ModelInvocationId,
                        DateTime.UtcNow);
                    var state = JsonSerializer.Serialize(new CheckpointState(decision.ActorCount, decision.Strategy, decision.TimingAdjustmentMs, evidence.RequestsConsumed, evidence.ModelCallsConsumed));
                    await decisionCheckpoints.PersistAsync(
                        workId,
                        leaseOwner,
                        iterationRecord,
                        new WorkCheckpoint("agent-decision-persisted", iteration, state, iterationRecord.OccurredAtUtc),
                        $"Iteration {iteration}: {decision.Action} using {decision.Strategy}.",
                        token);
                    run = await runs.GetAsync(run.Id, token) ?? throw new InvalidOperationException("The run disappeared after its decision checkpoint.");
                },
                async (iteration, settings, evidence, token) =>
                {
                    var state = JsonSerializer.Serialize(new CheckpointState(
                        settings.ActorCount,
                        settings.Strategy,
                        settings.TimingAdjustmentMs,
                        evidence.RequestsConsumed,
                        evidence.ModelCallsConsumed,
                        evidence.InvariantOutcome,
                        evidence.TraceReferences));
                    await inbox.SaveCheckpointAsync(
                        workId,
                        leaseOwner,
                        new WorkCheckpoint("attempt-completed", iteration, state, DateTime.UtcNow),
                        token);
                });

            run.AppendEvent(result.Outcome switch
            {
                CampaignOutcome.VerifiedViolation => "deterministic-violation-observed",
                CampaignOutcome.CompletedWithoutFinding => "campaign-no-finding",
                CampaignOutcome.BudgetExhausted => "budget-exhausted",
                CampaignOutcome.ModelFailed => "model-failed",
                CampaignOutcome.WorkerFailed => "worker-failed",
                _ => throw new ArgumentOutOfRangeException()
            }, $"Campaign stopped with explicit outcome {result.Outcome}.", DateTime.UtcNow);
            if (result.Outcome == CampaignOutcome.ModelFailed) run.Fail(DateTime.UtcNow);
            else run.Complete(DateTime.UtcNow);
            await runs.SaveAsync(run, CancellationToken.None);
        }
        catch (OperationCanceledException) when (bounded.IsCancellationRequested)
        {
            var durable = await runs.GetAsync(runId, CancellationToken.None) ?? run;
            if (durable.Status == RunStatus.Running)
            {
                durable.AppendEvent("budget-exhausted", "Campaign duration budget or cancellation boundary stopped execution.", DateTime.UtcNow);
                if (durable.CancellationRequestedAtUtc.HasValue) durable.Cancel(DateTime.UtcNow);
                else durable.Fail(DateTime.UtcNow);
                await runs.SaveAsync(durable, CancellationToken.None);
            }
            throw;
        }
        catch (Exception exception)
        {
            var durable = await runs.GetAsync(runId, CancellationToken.None) ?? run;
            if (durable.Status == RunStatus.Running)
            {
                durable.AppendEvent("worker-failed", $"Worker failed with category {Classify(exception)}.", DateTime.UtcNow);
                durable.Fail(DateTime.UtcNow);
                await runs.SaveAsync(durable, CancellationToken.None);
            }
            throw;
        }
    }

    private static InvariantDefinition ToInvariant(PlannedInvariant invariant) => invariant.Type switch
    {
        "numeric-boundary" => new NumericBoundaryInvariant(invariant.Metric, invariant.Maximum ?? throw new InvalidOperationException("A numeric maximum is required.")),
        "cardinality" => new CardinalityInvariant(invariant.Metric),
        _ => throw new InvalidOperationException("The approved invariant type is not executable in this phase.")
    };

    private static string Classify(Exception exception) => exception switch
    {
        ModelOutputException => "model",
        HttpRequestException => "transport",
        InvalidOperationException => "orchestration",
        _ => "worker"
    };

    private static RecoveryState RecoverSettings(WorkCheckpoint? checkpoint, ScenarioPlan plan)
    {
        if (checkpoint is null)
            return new RecoveryState(new CampaignSettings(plan.Strategy.ActorCount, plan.Strategy.Kind, 0), 0, 0, 0, null, false);
        var state = JsonSerializer.Deserialize<CheckpointState>(checkpoint.StateJson)
            ?? throw new InvalidOperationException("The persisted campaign checkpoint is invalid.");
        var settings = new CampaignSettings(state.ActorCount, state.Strategy, state.TimingAdjustmentMs);
        if (checkpoint.Boundary == "agent-decision-persisted")
            return new RecoveryState(settings, checkpoint.Iteration, state.RequestsConsumed, state.ModelCallsConsumed, null, true);
        if (checkpoint.Boundary == "attempt-completed" && state.InvariantOutcome is not null)
        {
            var outcome = Enum.Parse<InvariantOutcome>(state.InvariantOutcome);
            return new RecoveryState(
                settings,
                Math.Max(0, checkpoint.Iteration - 1),
                state.RequestsConsumed,
                state.ModelCallsConsumed,
                new DeterministicAttemptResult(outcome, state.TraceReferences ?? [], 0),
                true);
        }
        throw new InvalidOperationException("The persisted campaign checkpoint boundary is unsupported.");
    }

    private sealed record RecoveryState(
        CampaignSettings Settings,
        int ResumeAfterIteration,
        int RequestsConsumed,
        int ModelCallsConsumed,
        DeterministicAttemptResult? RecoveredAttempt,
        bool HasCheckpoint);

    private sealed record CheckpointState(
        int ActorCount,
        string Strategy,
        int TimingAdjustmentMs,
        int RequestsConsumed,
        int ModelCallsConsumed,
        string? InvariantOutcome = null,
        IReadOnlyList<string>? TraceReferences = null);
}
