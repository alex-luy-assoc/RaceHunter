using System.Collections.Concurrent;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Concurrency.Tracing;
using RaceHunter.Domain.Budgets;
using Xunit;

namespace RaceHunter.Concurrency.Tests;

public sealed class SchedulerTests
{
    [Fact]
    public void Simultaneous_start_assigns_zero_offset_to_every_actor()
    {
        var plan = new SimultaneousStartStrategy().Create(4, 17);

        Assert.All(plan.Actors, actor => Assert.Equal(TimeSpan.Zero, actor.Offset));
    }

    [Fact]
    public void Seeded_jitter_is_reproducible_for_same_seed()
    {
        var strategy = new SeededJitterStrategy(TimeSpan.FromMilliseconds(25));

        Assert.Equal(strategy.Create(8, 42).Actors, strategy.Create(8, 42).Actors);
    }

    [Fact]
    public async Task Scheduler_cancels_in_flight_work_at_duration_budget()
    {
        var scheduler = new ConcurrencyScheduler(1, 1);
        var budget = new ExperimentBudget(1, 1, 1, 0, TimeSpan.FromMilliseconds(50), 0);

        var result = await scheduler.ExecuteAsync(new SimultaneousStartStrategy().Create(1, 1), budget,
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                return TargetCallResult.Success();
            }, CancellationToken.None);

        Assert.Equal(BudgetStopReason.DurationExhausted, result.StopReason);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public void Seeded_jitter_never_exceeds_configured_offset()
    {
        var plan = new SeededJitterStrategy(TimeSpan.FromMilliseconds(25)).Create(100, 42);

        Assert.All(plan.Actors, actor => Assert.InRange(actor.Offset, TimeSpan.Zero, TimeSpan.FromMilliseconds(25)));
        Assert.All(plan.Actors, actor => Assert.Equal(0, actor.Offset.Ticks % TimeSpan.TicksPerMillisecond));
    }

    [Fact]
    public void Checkpoint_strategy_assigns_a_deterministic_actor_order()
    {
        var strategy = new CheckpointStrategy();

        Assert.Equal([1, 2, 3, 4], strategy.Create(4, 99).Actors.Select(actor => actor.CheckpointOrder));
        Assert.Equal(strategy.Create(4, 99).Actors, strategy.Create(4, 99).Actors);
    }

    [Fact]
    public async Task Simultaneous_start_barrier_releases_only_after_every_actor_arrives()
    {
        var barrier = new AsyncStartBarrier(3);
        var first = barrier.SignalAndWaitAsync(CancellationToken.None);
        var second = barrier.SignalAndWaitAsync(CancellationToken.None);

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await barrier.SignalAndWaitAsync(CancellationToken.None);
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Scheduler_respects_global_concurrency_ceiling() =>
        Assert.Equal(2, await MeasurePeakAsync(global: 2, target: 4, experiment: 4));

    [Fact]
    public async Task Scheduler_respects_target_concurrency_ceiling() =>
        Assert.Equal(2, await MeasurePeakAsync(global: 4, target: 2, experiment: 4));

    [Fact]
    public async Task Scheduler_respects_experiment_concurrency_ceiling() =>
        Assert.Equal(2, await MeasurePeakAsync(global: 4, target: 4, experiment: 2));

    [Fact]
    public async Task Scheduler_supports_one_hundred_logical_actors_without_exceeding_any_ceiling()
    {
        var scheduler = new ConcurrencyScheduler(globalConcurrency: 9, targetConcurrency: 7);
        var budget = new ExperimentBudget(100, 5, 100, 1, TimeSpan.FromSeconds(30), 0);
        var current = 0;
        var peak = 0;

        var result = await scheduler.ExecuteAsync(new SeededJitterStrategy(TimeSpan.Zero).Create(100, 4242), budget, async (_, token) =>
        {
            var active = Interlocked.Increment(ref current);
            int observed;
            while (active > (observed = Volatile.Read(ref peak))) Interlocked.CompareExchange(ref peak, active, observed);
            await Task.Delay(2, token);
            Interlocked.Decrement(ref current);
            return TargetCallResult.Success();
        }, CancellationToken.None);

        Assert.Equal(100, result.Executions.Count);
        Assert.InRange(peak, 1, 5);
        Assert.Equal(BudgetStopReason.None, result.StopReason);
    }

    [Fact]
    public async Task Scheduler_stops_starting_work_when_request_budget_is_exhausted()
    {
        var scheduler = new ConcurrencyScheduler(4, 4);
        var budget = new ExperimentBudget(6, 4, 3, 1, TimeSpan.FromMinutes(1), 0);
        var executions = 0;

        var result = await scheduler.ExecuteAsync(new SimultaneousStartStrategy().Create(6, 1), budget,
            (_, _) => { Interlocked.Increment(ref executions); return Task.FromResult(TargetCallResult.Success()); }, CancellationToken.None);

        Assert.Equal(3, executions);
        Assert.Equal(BudgetStopReason.RequestsExhausted, result.StopReason);
    }

    [Fact]
    public async Task Scheduler_starts_no_new_work_after_cancellation_is_observed()
    {
        var scheduler = new ConcurrencyScheduler(1, 1);
        var budget = new ExperimentBudget(5, 1, 5, 1, TimeSpan.FromMinutes(1), 0);
        using var cancellation = new CancellationTokenSource();
        var executions = 0;

        var result = await scheduler.ExecuteAsync(new SeededJitterStrategy(TimeSpan.Zero).Create(5, 1), budget,
            (_, _) =>
            {
                Interlocked.Increment(ref executions);
                cancellation.Cancel();
                return Task.FromResult(TargetCallResult.Success());
            }, cancellation.Token);

        Assert.Equal(1, executions);
        Assert.True(result.Cancelled);
    }

    [Fact]
    public async Task Trace_collector_assigns_unique_monotonic_sequences_under_concurrency()
    {
        var collector = new TraceCollector();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(actor => Task.Run(() =>
            collector.Append(Guid.Empty, Guid.Empty, actor, "order", "completed", $"request-{actor}", DateTime.UnixEpoch))));

        Assert.Equal(Enumerable.Range(1, 100).Select(value => (long)value), collector.Snapshot().Select(item => item.Sequence));
    }

    [Fact]
    public void Trace_collector_preserves_correlation_metadata()
    {
        var collector = new TraceCollector();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        collector.Append(runId, attemptId, 3, "observe", "response", "request-7", DateTime.UnixEpoch);

        var trace = Assert.Single(collector.Snapshot());
        Assert.Equal((runId, attemptId, 3, "observe", "request-7"), (trace.RunId, trace.AttemptId, trace.ActorId, trace.StepId, trace.RequestId));
    }

    private static async Task<int> MeasurePeakAsync(int global, int target, int experiment)
    {
        var scheduler = new ConcurrencyScheduler(global, target);
        var budget = new ExperimentBudget(8, experiment, 8, 1, TimeSpan.FromMinutes(1), 0);
        var current = 0;
        var peak = 0;
        await scheduler.ExecuteAsync(new SeededJitterStrategy(TimeSpan.Zero).Create(8, 1), budget, async (_, cancellationToken) =>
        {
            var active = Interlocked.Increment(ref current);
            int observed;
            while (active > (observed = Volatile.Read(ref peak))) Interlocked.CompareExchange(ref peak, active, observed);
            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref current);
            return TargetCallResult.Success();
        }, CancellationToken.None);
        return peak;
    }
}
