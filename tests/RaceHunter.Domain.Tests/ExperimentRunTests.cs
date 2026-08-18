using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Common;
using RaceHunter.Domain.Runs;
using Xunit;

namespace RaceHunter.Domain.Tests;

public sealed class ExperimentRunTests
{
    [Fact]
    public void Public_sandbox_budget_has_approved_hard_limits()
    {
        var budget = ExperimentBudget.PublicSandbox;

        Assert.Equal(10, budget.MaxActors);
        Assert.Equal(40, budget.MaxRequests);
        Assert.Equal(5, budget.MaxModelCalls);
        Assert.Equal(TimeSpan.FromSeconds(90), budget.MaxDuration);
    }

    [Fact]
    public void Authenticated_budget_rejects_more_than_one_hundred_actors() =>
        Assert.Throws<DomainException>(() => new ExperimentBudget(101, 10, 200, 5, TimeSpan.FromMinutes(1), 1));

    [Fact]
    public void Budget_rejects_experiment_concurrency_above_actor_count() =>
        Assert.Throws<DomainException>(() => new ExperimentBudget(4, 5, 20, 1, TimeSpan.FromSeconds(30), 0));

    [Fact]
    public void Budget_ledger_refuses_request_after_ceiling()
    {
        var ledger = new BudgetLedger(new ExperimentBudget(2, 2, 2, 1, TimeSpan.FromSeconds(30), 0), DateTime.UnixEpoch);

        Assert.True(ledger.TryConsumeRequest(DateTime.UnixEpoch));
        Assert.True(ledger.TryConsumeRequest(DateTime.UnixEpoch));
        Assert.False(ledger.TryConsumeRequest(DateTime.UnixEpoch));
        Assert.Equal(BudgetStopReason.RequestsExhausted, ledger.StopReason);
    }

    [Fact]
    public void Budget_ledger_stops_when_duration_is_reached()
    {
        var ledger = new BudgetLedger(new ExperimentBudget(1, 1, 10, 1, TimeSpan.FromSeconds(5), 0), DateTime.UnixEpoch);

        Assert.False(ledger.TryConsumeRequest(DateTime.UnixEpoch.AddSeconds(5)));
        Assert.Equal(BudgetStopReason.DurationExhausted, ledger.StopReason);
    }

    [Fact]
    public void Run_records_legal_lifecycle_and_ordered_progress()
    {
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UnixEpoch);

        run.Start(DateTime.UnixEpoch.AddSeconds(1));
        var first = run.AppendEvent("attempt-started", "Attempt 1 started", DateTime.UnixEpoch.AddSeconds(2));
        var second = run.AppendEvent("invariant-failed", "Oversell observed", DateTime.UnixEpoch.AddSeconds(3));
        run.Complete(DateTime.UnixEpoch.AddSeconds(4));

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(1, first.Cursor);
        Assert.Equal(2, second.Cursor);
        Assert.Equal(2, run.Events.Count);
    }

    [Fact]
    public void Cancellation_is_idempotent_and_terminal_state_is_immutable()
    {
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UnixEpoch);
        run.Start(DateTime.UnixEpoch);

        Assert.True(run.RequestCancellation(DateTime.UnixEpoch.AddSeconds(1)));
        Assert.False(run.RequestCancellation(DateTime.UnixEpoch.AddSeconds(2)));
        run.Cancel(DateTime.UnixEpoch.AddSeconds(2));

        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.Throws<DomainException>(() => run.Complete(DateTime.UnixEpoch.AddSeconds(3)));
    }

    [Fact]
    public void Finding_lifecycle_records_ordered_reproduction_and_minimization_transitions()
    {
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UnixEpoch);
        run.Start(DateTime.UnixEpoch.AddSeconds(1));

        Assert.True(run.BeginReproduction(DateTime.UnixEpoch.AddSeconds(2)));
        Assert.True(run.BeginMinimization(DateTime.UnixEpoch.AddSeconds(3)));

        Assert.Equal(RunStatus.Minimizing, run.Status);
        Assert.Equal(
            [(1L, "reproduction-started"), (2L, "minimization-started")],
            run.Events.Select(item => (item.Cursor, item.Kind)));
    }

    [Fact]
    public void Rehydrated_finding_lifecycle_does_not_duplicate_or_regress_transitions()
    {
        var run = ExperimentRun.Rehydrate(
            Guid.NewGuid(),
            ExperimentBudget.PublicSandbox,
            RunStatus.Minimizing,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddSeconds(1),
            null,
            null,
            [
                new RunEvent(1, "reproduction-started", "Reproduction started.", DateTime.UnixEpoch.AddSeconds(2)),
                new RunEvent(2, "minimization-started", "Minimization started.", DateTime.UnixEpoch.AddSeconds(3))
            ]);

        Assert.False(run.BeginReproduction(DateTime.UnixEpoch.AddSeconds(4)));
        Assert.False(run.BeginMinimization(DateTime.UnixEpoch.AddSeconds(5)));
        Assert.Equal(RunStatus.Minimizing, run.Status);
        Assert.Equal(2, run.Events.Count);
    }

    [Theory]
    [InlineData(RunStatus.Reproducing)]
    [InlineData(RunStatus.Minimizing)]
    public void Active_finding_phases_can_finish_and_remain_terminally_immutable(RunStatus phase)
    {
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UnixEpoch);
        run.Start(DateTime.UnixEpoch.AddSeconds(1));
        run.BeginReproduction(DateTime.UnixEpoch.AddSeconds(2));
        if (phase == RunStatus.Minimizing) run.BeginMinimization(DateTime.UnixEpoch.AddSeconds(3));

        run.Complete(DateTime.UnixEpoch.AddSeconds(4));

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Throws<DomainException>(() => run.BeginReproduction(DateTime.UnixEpoch.AddSeconds(5)));
        Assert.Throws<DomainException>(() => run.BeginMinimization(DateTime.UnixEpoch.AddSeconds(5)));
    }
}
