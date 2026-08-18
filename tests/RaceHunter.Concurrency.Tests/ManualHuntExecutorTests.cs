using System.Diagnostics;
using RaceHunter.Application.Abstractions;
using RaceHunter.Concurrency.Execution;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Runs;
using Xunit;

namespace RaceHunter.Concurrency.Tests;

public sealed class ManualHuntExecutorTests
{
    [Fact]
    public async Task Manual_hunt_persists_live_progress_and_completes_with_deterministic_result()
    {
        var store = new MemoryRunStore();
        var executor = new ManualHuntExecutor(new ConcurrencyScheduler(4, 4), store, store, store, store);
        var request = new ManualHuntRequest(Guid.NewGuid(), new ExperimentBudget(2, 2, 2, 1, TimeSpan.FromSeconds(30), 0),
            ScheduleKind.SimultaneousStart, 123, new NumericBoundaryInvariant("successful-orders", 1));

        var run = await executor.ExecuteAsync(request,
            (_, _) => Task.FromResult(TargetCallResult.Success([Observation.Number("successful-orders", 2, "trace-result")])), CancellationToken.None);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(InvariantOutcome.Fail, run.InvariantOutcome);
        Assert.Contains((await store.GetEventsAsync(run.Id, 0, CancellationToken.None)), item => item.Kind == "invariant-failed");
    }

    [Fact]
    public async Task Manual_hunt_persists_cancelled_outcome_without_finding()
    {
        var store = new MemoryRunStore();
        var executor = new ManualHuntExecutor(new ConcurrencyScheduler(1, 1), store, store, store, store);
        using var cancellation = new CancellationTokenSource();
        var request = new ManualHuntRequest(Guid.NewGuid(), new ExperimentBudget(3, 1, 3, 1, TimeSpan.FromSeconds(30), 0),
            ScheduleKind.SeededJitter, 123, new NumericBoundaryInvariant("successful-orders", 1));

        var run = await executor.ExecuteAsync(request, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(TargetCallResult.Success());
        }, cancellation.Token);

        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.Null(run.InvariantOutcome);
    }

    [Fact]
    public async Task Durable_cancellation_is_observed_within_two_seconds()
    {
        var store = new MemoryRunStore();
        var executor = new ManualHuntExecutor(new ConcurrencyScheduler(1, 1), store, store, store, store);
        var runId = Guid.NewGuid();
        var request = new ManualHuntRequest(runId, new ExperimentBudget(2, 1, 2, 1, TimeSpan.FromSeconds(30), 0),
            ScheduleKind.SeededJitter, 123, new NumericBoundaryInvariant("successful-orders", 1));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = executor.ExecuteAsync(request, async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return TargetCallResult.Success();
        }, CancellationToken.None);
        await started.Task;
        var stopwatch = Stopwatch.StartNew();
        var persisted = await store.GetAsync(runId, CancellationToken.None);
        var requestedAtUtc = DateTime.UtcNow;
        persisted!.RequestCancellation(requestedAtUtc);
        await store.SaveAsync(persisted, CancellationToken.None);

        var result = await execution;

        Assert.Equal(RunStatus.Cancelled, result.Status);
        Assert.Equal(requestedAtUtc, result.Run.CancellationRequestedAtUtc);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Cancellation took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Target_failure_becomes_a_durable_terminal_outcome()
    {
        var store = new MemoryRunStore();
        var executor = new ManualHuntExecutor(new ConcurrencyScheduler(1, 1), store, store, store, store);
        var request = new ManualHuntRequest(Guid.NewGuid(), new ExperimentBudget(1, 1, 1, 0, TimeSpan.FromSeconds(30), 0),
            ScheduleKind.SimultaneousStart, 1, new NumericBoundaryInvariant("successful-orders", 1));

        var result = await executor.ExecuteAsync(request,
            (_, _) => throw new HttpRequestException("Controlled target failure."), CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains((await store.GetEventsAsync(result.Id, 0, CancellationToken.None)), item => item.Kind == "execution-failed");
    }

    [Fact]
    public async Task Cancellation_probe_failure_becomes_a_durable_terminal_outcome()
    {
        var store = new MemoryRunStore();
        var executor = new ManualHuntExecutor(new ConcurrencyScheduler(1, 1), store, new ThrowingCancellationProbe(), store, store);
        var request = new ManualHuntRequest(Guid.NewGuid(), new ExperimentBudget(1, 1, 1, 0, TimeSpan.FromSeconds(30), 0),
            ScheduleKind.SimultaneousStart, 1, new NumericBoundaryInvariant("successful-orders", 1));

        var result = await executor.ExecuteAsync(request, async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return TargetCallResult.Success();
        }, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains((await store.GetEventsAsync(result.Id, 0, CancellationToken.None)), item => item.Kind == "execution-failed");
    }

    private sealed class MemoryRunStore : IRunStore, IRunCancellationProbe, ITraceStore, IRunAttemptStore
    {
        private readonly Dictionary<Guid, ExperimentRun> runs = [];
        public Task AddAsync(ExperimentRun run, CancellationToken cancellationToken) { runs.Add(run.Id, run); return Task.CompletedTask; }
        public Task SaveAsync(ExperimentRun run, CancellationToken cancellationToken) { runs[run.Id] = run; return Task.CompletedTask; }
        public Task<ExperimentRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(runs.GetValueOrDefault(id));
        public Task<IReadOnlyList<RunEvent>> GetEventsAsync(Guid id, long after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunEvent>>(runs[id].Events.Where(item => item.Cursor > after).ToArray());
        public Task<bool> RequestCancellationAsync(Guid id, DateTime requestedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(runs[id].RequestCancellation(requestedAtUtc));
        public Task<DateTime?> GetRequestedAtUtcAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult(runs[runId].CancellationRequestedAtUtc);
        public Task AppendAsync(RaceHunter.Domain.Tracing.TraceEvent traceEvent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<RaceHunter.Domain.Tracing.TraceEvent>> GetAsync(Guid runId, long after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RaceHunter.Domain.Tracing.TraceEvent>>([]);
        public Task AddAsync(RunAttempt attempt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(RunAttempt attempt, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingCancellationProbe : IRunCancellationProbe
    {
        public Task<DateTime?> GetRequestedAtUtcAsync(Guid runId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Controlled probe failure.");
    }
}
