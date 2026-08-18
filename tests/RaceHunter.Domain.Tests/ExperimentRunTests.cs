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
}
