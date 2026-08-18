using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;

namespace RaceHunter.Concurrency.Scheduling;

public sealed record TargetCallResult(bool Succeeded, IReadOnlyList<Observation> Observations, string? RequestId, bool Reused = false)
{
    public static TargetCallResult Success(IReadOnlyList<Observation>? observations = null, string? requestId = null, bool reused = false) => new(true, observations ?? [], requestId, reused);
    public static TargetCallResult Failure(IReadOnlyList<Observation>? observations = null, string? requestId = null, bool reused = false) => new(false, observations ?? [], requestId, reused);
}

public sealed record ActorExecutionResult(ScheduledActor Actor, TargetCallResult TargetResult);

public sealed record ScheduleExecutionResult(
    IReadOnlyList<ActorExecutionResult> Executions,
    BudgetStopReason StopReason,
    bool Cancelled);

public sealed class ConcurrencyScheduler
{
    private readonly SemaphoreSlim globalLimiter;
    private readonly SemaphoreSlim targetLimiter;

    public ConcurrencyScheduler(int globalConcurrency, int targetConcurrency)
    {
        if (globalConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(globalConcurrency));
        if (targetConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(targetConcurrency));
        globalLimiter = new SemaphoreSlim(globalConcurrency, globalConcurrency);
        targetLimiter = new SemaphoreSlim(targetConcurrency, targetConcurrency);
    }

    public async Task<ScheduleExecutionResult> ExecuteAsync(
        SchedulePlan plan,
        ExperimentBudget budget,
        Func<ScheduledActor, CancellationToken, Task<TargetCallResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (plan.Actors.Count > budget.MaxActors) throw new ArgumentException("The schedule exceeds the actor budget.", nameof(plan));

        var startedAtUtc = DateTime.UtcNow;
        var ledger = new BudgetLedger(budget, startedAtUtc);
        using var boundedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        boundedCancellation.CancelAfter(budget.MaxDuration);
        var executionToken = boundedCancellation.Token;
        var experimentLimiter = new SemaphoreSlim(budget.MaxConcurrentActors, budget.MaxConcurrentActors);
        var executions = new List<ActorExecutionResult>();
        var resultLock = new object();
        var startGate = new object();
        var cancellationObserved = false;
        using var cancellationRegistration = executionToken.Register(() =>
        {
            lock (startGate) cancellationObserved = true;
        });
        var startBarrier = plan.Kind == ScheduleKind.SimultaneousStart ? new AsyncStartBarrier(plan.Actors.Count) : null;

        async Task ExecuteActorAsync(ScheduledActor actor)
        {
            var globalAcquired = false;
            var targetAcquired = false;
            var experimentAcquired = false;
            try
            {
                if (actor.Offset > TimeSpan.Zero) await Task.Delay(actor.Offset, executionToken);
                if (startBarrier is not null) await startBarrier.SignalAndWaitAsync(executionToken);
                await globalLimiter.WaitAsync(executionToken);
                globalAcquired = true;
                await targetLimiter.WaitAsync(executionToken);
                targetAcquired = true;
                await experimentLimiter.WaitAsync(executionToken);
                experimentAcquired = true;
                executionToken.ThrowIfCancellationRequested();
                if (!ledger.TryConsumeRequest(DateTime.UtcNow)) return;

                Task<TargetCallResult> pendingOperation;
                lock (startGate)
                {
                    if (cancellationObserved) return;
                    pendingOperation = operation(actor, executionToken);
                }
                var targetResult = await pendingOperation;
                lock (resultLock) executions.Add(new ActorExecutionResult(actor, targetResult));
            }
            catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
            {
            }
            finally
            {
                if (experimentAcquired) experimentLimiter.Release();
                if (targetAcquired) targetLimiter.Release();
                if (globalAcquired) globalLimiter.Release();
            }
        }

        await Task.WhenAll(plan.Actors.Select(ExecuteActorAsync));
        var cancelled = cancellationToken.IsCancellationRequested;
        if (cancelled) ledger.MarkCancelled();
        else if (boundedCancellation.IsCancellationRequested) ledger.MarkDurationExhausted();
        return new ScheduleExecutionResult(executions.OrderBy(item => item.Actor.ActorId).ToArray(), ledger.StopReason, cancelled);
    }
}

public sealed class AsyncStartBarrier
{
    private readonly int participants;
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;

    public AsyncStartBarrier(int participants)
    {
        if (participants < 1) throw new ArgumentOutOfRangeException(nameof(participants));
        this.participants = participants;
    }

    public Task SignalAndWaitAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref arrivals) == participants) release.TrySetResult();
        return release.Task.WaitAsync(cancellationToken);
    }
}
