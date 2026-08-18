using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Concurrency.Minimization;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Domain.Findings;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using RaceHunter.Domain.Runs;

namespace RaceHunter.Worker.Execution;

internal sealed class CampaignRunner(
    IHuntStore hunts,
    IRunStore runs,
    IWorkInbox inbox,
    IAgentDecisionCheckpointStore decisionCheckpoints,
    IExperimentStrategist strategist,
    ReferenceCampaignAttemptExecutor attemptExecutor,
    ReferenceInventoryTargetClient target,
    IFindingStore findings,
    IFindingProbeCheckpointStore probeCheckpoints,
    ITraceStore traces,
    IConfiguration configuration) : ICampaignWorkHandler
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

        var now = DateTime.UtcNow;
        var remainingDuration = CampaignBudgetWindow.Remaining(
            run.StartedAtUtc ?? now,
            run.Budget.MaxDuration,
            now);
        if (remainingDuration == TimeSpan.Zero)
        {
            run.AppendEvent("budget-exhausted", "The cumulative campaign duration budget was exhausted before recovery could resume.", now);
            run.Fail(now);
            await runs.SaveAsync(run, CancellationToken.None);
            return;
        }

        using var timeout = new CancellationTokenSource(remainingDuration);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
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
        var invariant = PlannedInvariantCompiler.Compile(plan.Invariant);
        try
        {
            AdaptiveCampaignResult result;
            if (recovered.FinalizeFinding)
            {
                result = new AdaptiveCampaignResult(
                    CampaignOutcome.VerifiedViolation,
                    true,
                    recovered.ResumeAfterIteration,
                    recovered.ModelCallsConsumed,
                    [],
                    recovered.FailedSettings,
                    recovered.RecoveredAttempt);
            }
            else
            {
                result = await new AdaptiveStrategyLoop(strategist).RunAsync(
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
                    var state = JsonSerializer.Serialize(new CheckpointState(
                        decision.ActorCount,
                        decision.Strategy,
                        decision.TimingAdjustmentMs,
                        evidence.RequestsConsumed,
                        evidence.ModelCallsConsumed,
                        evidence.InvariantOutcome,
                        evidence.TraceReferences,
                        evidence.Schedule,
                        decision.Action.ToString(),
                        evidence.AttemptSettings));
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
                        evidence.TraceReferences,
                        evidence.Schedule));
                    await inbox.SaveCheckpointAsync(
                        workId,
                        leaseOwner,
                        new WorkCheckpoint("attempt-completed", iteration, state, DateTime.UtcNow),
                        token);
                });
            }

            if (result.Outcome == CampaignOutcome.VerifiedViolation)
            {
                var findingId = await FinalizeFindingAsync(run, plan, invariant, result, attemptExecutor, target, findings, probeCheckpoints, traces, configuration, bounded.Token);
                run.AppendEvent(
                    findingId.HasValue ? "finding-ready" : "reproduction-inconclusive",
                    findingId.HasValue
                        ? $"Verified finding {findingId.Value} is ready with immutable replay evidence."
                        : "The deterministic violation did not meet the reference reproduction threshold within the request budget.",
                    DateTime.UtcNow);
            }
            else
            {
                run.AppendEvent(result.Outcome switch
                {
                    CampaignOutcome.CompletedWithoutFinding => "campaign-no-finding",
                    CampaignOutcome.BudgetExhausted => "budget-exhausted",
                    CampaignOutcome.ModelFailed => "model-failed",
                    CampaignOutcome.WorkerFailed => "worker-failed",
                    _ => throw new ArgumentOutOfRangeException()
                }, $"Campaign stopped with explicit outcome {result.Outcome}.", DateTime.UtcNow);
            }
            if (result.Outcome == CampaignOutcome.ModelFailed) run.Fail(DateTime.UtcNow);
            else run.Complete(DateTime.UtcNow);
            await runs.SaveAsync(run, CancellationToken.None);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var durable = await runs.GetAsync(runId, CancellationToken.None) ?? run;
            if (durable.Status == RunStatus.Running)
            {
                durable.AppendEvent("budget-exhausted", "Campaign duration budget or cancellation boundary stopped execution.", DateTime.UtcNow);
                if (durable.CancellationRequestedAtUtc.HasValue) durable.Cancel(DateTime.UtcNow);
                else durable.Fail(DateTime.UtcNow);
                await runs.SaveAsync(durable, CancellationToken.None);
            }
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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

    private static string Classify(Exception exception) => exception switch
    {
        ModelOutputException => "model",
        HttpRequestException => "transport",
        InvalidOperationException => "orchestration",
        _ => "worker"
    };

    private static async Task<Guid?> FinalizeFindingAsync(
        ExperimentRun run,
        ScenarioPlan plan,
        InvariantDefinition invariant,
        AdaptiveCampaignResult result,
        ReferenceCampaignAttemptExecutor attemptExecutor,
        ReferenceInventoryTargetClient target,
        IFindingStore findings,
        IFindingProbeCheckpointStore probeCheckpoints,
        ITraceStore traces,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var existingFindingId = await findings.GetIdByRunAsync(run.Id, cancellationToken);
        if (existingFindingId.HasValue) return existingFindingId;
        var failedSettings = result.FailedSettings ?? throw new InvalidOperationException("Verified campaign evidence is missing its failed settings.");
        var failedAttempt = result.FailedAttempt ?? throw new InvalidOperationException("Verified campaign evidence is missing its deterministic attempt.");
        var original = CreateReplayCandidate(failedSettings, failedAttempt, plan.Strategy.Seed);
        var persistedTraces = await traces.GetAsync(run.Id, 0, cancellationToken);
        var persistedRequestIds = persistedTraces.Select(item => item.RequestId).ToHashSet(StringComparer.Ordinal);
        var demoControlKey = configuration["ReferenceTarget:DemoControlKey"]
            ?? throw new InvalidOperationException("ReferenceTarget:DemoControlKey is required for measured reproduction.");
        var probe = new DurableFindingReplayProbe(
            run.Id,
            Math.Max(0, run.Budget.MaxRequests - persistedTraces.Count),
            probeCheckpoints,
            (probeKey, candidate, _, token) => target.CountMissingOrdersAsync(
                candidate,
                $"{run.Id:N}:{probeKey}",
                demoControlKey,
                persistedRequestIds,
                token),
            async (probeKey, candidate, mode, token) =>
            {
                await target.ResetAsync(mode, demoControlKey, $"{run.Id:N}:{probeKey}:reset", token);
                var attempt = await attemptExecutor.ExecuteReplayAsync(
                    run.Id,
                    run.Budget,
                    invariant,
                    candidate,
                    $"{run.Id:N}:{probeKey}",
                    token);
                return new ReplayObservation(attempt.InvariantOutcome, attempt.TraceReferences, attempt.RequestsConsumed);
            });
        var reproduction = await new ReproductionVerifier().VerifyReferenceAsync(original, probe, cancellationToken);
        if (!reproduction.Verified) return null;
        var minimized = await new FailureMinimizer().MinimizeAsync(original, probe, cancellationToken);
        if (minimized.Candidate.ActorCount != 2) return null;

        var artifact = CreateReplayArtifact(run, plan, minimized.Candidate);
        var findingId = artifact.FindingId;
        var vulnerableReplay = await probe.ExecuteAsync("proof:vulnerable", minimized.Candidate, ReplayTargetMode.Vulnerable, cancellationToken);
        if (vulnerableReplay.Outcome != InvariantOutcome.Fail) return null;
        var finding = Finding.CreateReference(
            findingId,
            run.Id,
            artifact.InvariantVersionId,
            new InvariantResult(InvariantOutcome.Fail, failedAttempt.TraceReferences, "Successful orders exceeded available inventory."),
            reproduction.Attempts.Select(item => new ReproductionAttempt(item.Attempt, item.Outcome, item.TraceReferences)).ToArray(),
            artifact,
            artifact.CreatedAtUtc,
            "Gemini strategy activity is advisory; deterministic evaluator evidence verified and minimized this finding.");
        var vulnerableAttempt = ReplayAttempt.Complete(
            Guid.NewGuid(),
            artifact.Id,
            ReplayTargetMode.Vulnerable,
            vulnerableReplay.Outcome,
            vulnerableReplay.TraceReferences,
            artifact.Fingerprint,
            "reference-vulnerable-proof",
            DateTime.UtcNow);
        await findings.AddVerifiedAsync(finding, artifact, vulnerableAttempt, cancellationToken);
        return findingId;
    }

    internal static ReplayArtifact CreateReplayArtifact(ExperimentRun run, ScenarioPlan plan, ReplayCandidate candidate) => ReplayArtifact.Create(
        DeterministicId(run.Id, "replay-artifact"),
        DeterministicId(run.Id, "finding"),
        plan.PlanVersion,
        "invariant-v1",
        "reference-inventory:quantity=1",
        candidate.Strategy,
        candidate.Seed,
        candidate.Steps,
        "{\"quantity\":1}",
        run.StartedAtUtc ?? run.CreatedAtUtc);

    private static Guid DeterministicId(Guid runId, string purpose)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{runId:N}:{purpose}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    internal static ReplayCandidate CreateReplayCandidate(
        CampaignSettings failedSettings,
        DeterministicAttemptResult failedAttempt,
        int seed)
    {
        var exactSchedule = failedAttempt.Schedule
            ?? throw new InvalidOperationException("Verified campaign evidence is missing its exact executed schedule.");
        return new ReplayCandidate(
            failedSettings.Strategy,
            seed,
            exactSchedule.Select(step => new ReplayStep(step.ActorId, "place-order", "place-order", step.OffsetMilliseconds)));
    }

    internal static RecoveryState RecoverSettings(WorkCheckpoint? checkpoint, ScenarioPlan plan)
    {
        if (checkpoint is null)
            return new RecoveryState(new CampaignSettings(plan.Strategy.ActorCount, plan.Strategy.Kind, 0), 0, 0, 0, null, false, false, null);
        var state = JsonSerializer.Deserialize<CheckpointState>(checkpoint.StateJson)
            ?? throw new InvalidOperationException("The persisted campaign checkpoint is invalid.");
        var settings = new CampaignSettings(state.ActorCount, state.Strategy, state.TimingAdjustmentMs);
        if (checkpoint.Boundary == "agent-decision-persisted")
        {
            var shouldFinalize = state.Action == AgentActionKind.StartMinimization.ToString() &&
                state.InvariantOutcome == InvariantOutcome.Fail.ToString() &&
                state.Schedule is { Count: > 0 } &&
                state.AttemptSettings is not null;
            return new RecoveryState(
                settings,
                checkpoint.Iteration,
                state.RequestsConsumed,
                state.ModelCallsConsumed,
                shouldFinalize ? new DeterministicAttemptResult(InvariantOutcome.Fail, state.TraceReferences ?? [], 0, state.Schedule) : null,
                true,
                shouldFinalize,
                shouldFinalize ? state.AttemptSettings : null);
        }
        if (checkpoint.Boundary == "attempt-completed" && state.InvariantOutcome is not null)
        {
            var outcome = Enum.Parse<InvariantOutcome>(state.InvariantOutcome);
            return new RecoveryState(
                settings,
                Math.Max(0, checkpoint.Iteration - 1),
                state.RequestsConsumed,
                state.ModelCallsConsumed,
                new DeterministicAttemptResult(outcome, state.TraceReferences ?? [], 0, state.Schedule),
                true,
                false,
                null);
        }
        throw new InvalidOperationException("The persisted campaign checkpoint boundary is unsupported.");
    }

    internal sealed record RecoveryState(
        CampaignSettings Settings,
        int ResumeAfterIteration,
        int RequestsConsumed,
        int ModelCallsConsumed,
        DeterministicAttemptResult? RecoveredAttempt,
        bool HasCheckpoint,
        bool FinalizeFinding,
        CampaignSettings? FailedSettings);

    internal sealed record CheckpointState(
        int ActorCount,
        string Strategy,
        int TimingAdjustmentMs,
        int RequestsConsumed,
        int ModelCallsConsumed,
        string? InvariantOutcome = null,
        IReadOnlyList<string>? TraceReferences = null,
        IReadOnlyList<DeterministicReplayStep>? Schedule = null,
        string? Action = null,
        CampaignSettings? AttemptSettings = null);
}
