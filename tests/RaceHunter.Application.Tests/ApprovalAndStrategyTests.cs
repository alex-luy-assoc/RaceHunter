using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Common;
using RaceHunter.Domain.Invariants;
using Xunit;

namespace RaceHunter.Application.Tests;

public sealed class ApprovalAndStrategyTests
{
    [Fact]
    public async Task Approval_binds_the_exact_plan_version_to_created_run()
    {
        var store = new MemoryHuntWorkflowStore("plan-v1");
        var result = await new ApproveAndRun(store).ExecuteAsync(Guid.NewGuid(), "plan-v1", "approval-1", CancellationToken.None);
        Assert.Equal("plan-v1", result.PlanVersion);
        Assert.NotEqual(Guid.Empty, result.RunId);
    }

    [Fact]
    public async Task Approval_rejects_a_stale_plan_version()
    {
        var store = new MemoryHuntWorkflowStore("plan-v2");
        await Assert.ThrowsAsync<DomainException>(() =>
            new ApproveAndRun(store).ExecuteAsync(Guid.NewGuid(), "plan-v1", "approval-1", CancellationToken.None));
    }

    [Fact]
    public async Task Repeating_same_approval_key_returns_same_logical_run()
    {
        var store = new MemoryHuntWorkflowStore("plan-v1");
        var command = new ApproveAndRun(store);
        var huntId = Guid.NewGuid();
        var first = await command.ExecuteAsync(huntId, "plan-v1", "approval-1", CancellationToken.None);
        var duplicate = await command.ExecuteAsync(huntId, "plan-v1", "approval-1", CancellationToken.None);
        Assert.Equal(first.RunId, duplicate.RunId);
    }

    [Fact]
    public async Task A_second_distinct_approval_is_rejected()
    {
        var store = new MemoryHuntWorkflowStore("plan-v1");
        var command = new ApproveAndRun(store);
        var huntId = Guid.NewGuid();
        await command.ExecuteAsync(huntId, "plan-v1", "approval-1", CancellationToken.None);
        await Assert.ThrowsAsync<DomainException>(() => command.ExecuteAsync(huntId, "plan-v1", "approval-2", CancellationToken.None));
    }

    [Fact]
    public void Action_validator_rejects_non_allowlisted_strategy()
    {
        var decision = new StrategyDecision(AgentActionKind.SelectStrategy, 2, "unbounded-flood", 0, "No.", "strategy-v1", "gemini-3.5-flash", "i-1");
        Assert.Throws<AgentActionValidationException>(() => AgentActionValidator.Validate(decision, Context()));
    }

    [Fact]
    public void Action_validator_rejects_timing_outside_server_bound()
    {
        var decision = new StrategyDecision(AgentActionKind.AdjustTiming, 2, "seeded-jitter", 5001, "No.", "strategy-v1", "gemini-3.5-flash", "i-1");
        Assert.Throws<AgentActionValidationException>(() => AgentActionValidator.Validate(decision, Context()));
    }

    [Fact]
    public async Task Adaptive_loop_can_only_report_verified_when_deterministic_attempt_failed()
    {
        var loop = new AdaptiveStrategyLoop(new FixedStrategist(new StrategyDecision(AgentActionKind.Stop, 2, "simultaneous-start", 0, "Done.", "strategy-v1", "fake", "i-1")));
        var result = await loop.RunAsync(Context(), (_, _, _) => Task.FromResult(new DeterministicAttemptResult(InvariantOutcome.Pass, ["trace:1"])), CancellationToken.None);
        Assert.False(result.VerifiedViolation);
        Assert.Equal(CampaignOutcome.CompletedWithoutFinding, result.Outcome);

        var recoveredContext = new AdaptiveCampaignContext(
            Guid.NewGuid(),
            new CampaignSettings(2, "simultaneous-start", 0),
            ["simultaneous-start", "seeded-jitter", "checkpoint-interleaving"],
            ExperimentBudget.PublicSandbox,
            maxIterations: 3,
            requestsAlreadyConsumed: 2,
            recoveredAttempt: new DeterministicAttemptResult(InvariantOutcome.Fail, ["trace:recovered"]));
        var recovered = await loop.RunAsync(
            recoveredContext,
            (_, _, _) => throw new InvalidOperationException("A checkpointed deterministic attempt must not be executed again."),
            CancellationToken.None);
        Assert.True(recovered.VerifiedViolation);
        Assert.Equal(CampaignOutcome.VerifiedViolation, recovered.Outcome);

        var exhausted = await new AdaptiveStrategyLoop(new FailingStrategist())
            .RunAsync(Context(), (_, _, _) => Task.FromResult(new DeterministicAttemptResult(InvariantOutcome.Pass, ["trace:budget"], 2)), CancellationToken.None);
        Assert.Equal(CampaignOutcome.BudgetExhausted, exhausted.Outcome);
        Assert.Equal(2, exhausted.ModelCalls);
        Assert.Equal(2, exhausted.RequestsConsumed);
    }

    [Fact]
    public async Task Adaptive_loop_reserves_manual_setup_before_starting_an_attempt()
    {
        var budget = new ExperimentBudget(10, 10, 10, 1, TimeSpan.FromSeconds(30), 0);
        var context = new AdaptiveCampaignContext(Guid.NewGuid(), new CampaignSettings(10, "simultaneous-start", 0),
            ["simultaneous-start"], budget, 1, fixedRequestsPerAttempt: 1);
        var calls = 0;

        var result = await new AdaptiveStrategyLoop(new FixedStrategist(
                new StrategyDecision(AgentActionKind.Stop, 10, "simultaneous-start", 0, "Done.", "strategy-v1", "fake", "i-1")))
            .RunAsync(context, (_, _, _) => { calls++; return Task.FromResult(new DeterministicAttemptResult(InvariantOutcome.Pass, [])); }, CancellationToken.None);

        Assert.Equal(CampaignOutcome.BudgetExhausted, result.Outcome);
        Assert.Equal(0, calls);
        Assert.Equal(0, result.RequestsConsumed);
    }

    [Fact]
    public async Task Verified_attempt_is_preserved_when_the_iteration_budget_exhausts_before_strategist_stop()
    {
        var budget = new ExperimentBudget(2, 2, 3, 5, TimeSpan.FromSeconds(30), 0);
        var failed = new DeterministicAttemptResult(
            InvariantOutcome.Fail,
            ["trace:global-snapshot"],
            3,
            [new DeterministicReplayStep(1, 0), new DeterministicReplayStep(2, 0)]);
        var context = new AdaptiveCampaignContext(
            Guid.NewGuid(),
            new CampaignSettings(2, "simultaneous-start", 0),
            ["simultaneous-start"],
            budget,
            maxIterations: 1,
            fixedRequestsPerAttempt: 1);

        var result = await new AdaptiveStrategyLoop(new FixedStrategist(
                new StrategyDecision(AgentActionKind.Repeat, 2, "simultaneous-start", 0, "Repeat.", "strategy-v1", "fake", "i-1")))
            .RunAsync(context, (_, _, _) => Task.FromResult(failed), CancellationToken.None);

        Assert.Equal(CampaignOutcome.VerifiedViolation, result.Outcome);
        Assert.True(result.VerifiedViolation);
        Assert.Equal(context.Initial, result.FailedSettings);
        Assert.Same(failed, result.FailedAttempt);
        Assert.Equal(3, result.RequestsConsumed);
    }

    [Fact]
    public async Task Deterministic_failure_is_persisted_and_returned_without_another_strategy_decision()
    {
        var strategist = new CountingStrategist();
        var failed = new DeterministicAttemptResult(
            InvariantOutcome.Fail,
            ["trace:global-snapshot"],
            3,
            [new DeterministicReplayStep(1, 0), new DeterministicReplayStep(2, 0)]);
        var persisted = false;
        var decisionsPersisted = 0;

        var result = await new AdaptiveStrategyLoop(strategist).RunAsync(
            Context(),
            (_, _, _) => Task.FromResult(failed),
            CancellationToken.None,
            (_, _, _, _) => { decisionsPersisted++; return Task.CompletedTask; },
            (_, settings, evidence, _) =>
            {
                persisted = true;
                Assert.Equal(InvariantOutcome.Fail.ToString(), evidence.InvariantOutcome);
                Assert.Equal(settings, evidence.VerifiedFailedSettings);
                Assert.Same(failed, evidence.VerifiedFailedAttempt);
                return Task.CompletedTask;
            });

        Assert.True(persisted);
        Assert.Equal(0, strategist.Calls);
        Assert.Equal(0, decisionsPersisted);
        Assert.Equal(CampaignOutcome.VerifiedViolation, result.Outcome);
        Assert.True(result.VerifiedViolation);
        Assert.Equal(Context().Initial, result.FailedSettings);
        Assert.Same(failed, result.FailedAttempt);
        Assert.Equal(3, result.RequestsConsumed);
        Assert.Empty(result.Decisions);
    }

    [Fact]
    public void Campaign_duration_budget_uses_remaining_time_from_original_start()
    {
        var startedAt = DateTime.Parse("2026-08-18T12:00:00Z").ToUniversalTime();
        Assert.Equal(TimeSpan.FromSeconds(15), CampaignBudgetWindow.Remaining(startedAt, TimeSpan.FromSeconds(90), startedAt.AddSeconds(75)));
        Assert.Equal(TimeSpan.Zero, CampaignBudgetWindow.Remaining(startedAt, TimeSpan.FromSeconds(90), startedAt.AddSeconds(91)));
    }

    [Fact]
    public void Cross_observation_plan_compiles_to_deterministic_runtime_invariant()
    {
        var planned = new PlannedInvariant(
            "cross-observation",
            "successful-orders",
            null,
            "successful-orders",
            "inventory-capacity",
            "less-than-or-equal");
        var compiled = Assert.IsType<CrossObservationInvariant>(PlannedInvariantCompiler.Compile(planned));
        Assert.Equal("successful-orders", compiled.LeftMetric);
        Assert.Equal("inventory-capacity", compiled.RightMetric);
        Assert.Equal(CrossObservationRelation.LessThanOrEqual, compiled.Relation);
    }

    private static AdaptiveCampaignContext Context() => new(
        Guid.NewGuid(),
        new CampaignSettings(2, "simultaneous-start", 0),
        ["simultaneous-start", "seeded-jitter", "checkpoint-interleaving"],
        ExperimentBudget.PublicSandbox,
        maxIterations: 3);

    private sealed class FixedStrategist(StrategyDecision decision) : IExperimentStrategist
    {
        public Task<StrategyDecision> SelectNextAsync(StrategySelectionContext context, CancellationToken cancellationToken) => Task.FromResult(decision);
    }

    private sealed class FailingStrategist : IExperimentStrategist
    {
        public Task<StrategyDecision> SelectNextAsync(StrategySelectionContext context, CancellationToken cancellationToken) =>
            throw new ModelOutputException(ModelOutcome.BudgetExhausted, "repair budget exhausted", modelCallsConsumed: 2);
    }

    private sealed class CountingStrategist : IExperimentStrategist
    {
        public int Calls { get; private set; }

        public Task<StrategyDecision> SelectNextAsync(StrategySelectionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new StrategyDecision(
                AgentActionKind.Repeat,
                context.Current.ActorCount,
                context.Current.Strategy,
                context.Current.TimingAdjustmentMs,
                "Should not be called after deterministic failure.",
                "strategy-v1",
                "fake",
                "unexpected"));
        }
    }

    private sealed class MemoryHuntWorkflowStore(string planVersion) : IHuntWorkflowStore
    {
        private string? approvalKey;
        private Guid? runId;
        public Task<ApprovalResult> ApproveAndQueueAsync(Guid huntId, string requestedPlanVersion, string idempotencyKey, Guid requestedRunId, DateTime nowUtc, CancellationToken cancellationToken)
        {
            if (requestedPlanVersion != planVersion) throw new DomainException("The requested plan version is stale.");
            if (approvalKey is not null && approvalKey != idempotencyKey) throw new DomainException("The plan was already approved.");
            approvalKey ??= idempotencyKey;
            runId ??= requestedRunId;
            return Task.FromResult(new ApprovalResult(runId.Value, planVersion, approvalKey));
        }
    }
}
