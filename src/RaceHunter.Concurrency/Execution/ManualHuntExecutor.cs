using RaceHunter.Application.Abstractions;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Concurrency.Tracing;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Runs;

namespace RaceHunter.Concurrency.Execution;

public sealed record ManualHuntRequest(
    Guid RunId,
    ExperimentBudget Budget,
    ScheduleKind Schedule,
    int Seed,
    InvariantDefinition Invariant);

public sealed record ManualHuntExecutionResult(ExperimentRun Run, InvariantOutcome? InvariantOutcome)
{
    public Guid Id => Run.Id;
    public RunStatus Status => Run.Status;
}

public sealed class ManualHuntExecutor(
    ConcurrencyScheduler scheduler,
    IRunStore runStore,
    IRunCancellationProbe cancellationProbe,
    ITraceStore traceStore,
    IRunAttemptStore attemptStore)
{
    public async Task<ManualHuntExecutionResult> ExecuteAsync(
        ManualHuntRequest request,
        Func<ScheduledActor, CancellationToken, Task<TargetCallResult>> targetOperation,
        CancellationToken cancellationToken)
    {
        var run = ExperimentRun.Queue(request.RunId, request.Budget, DateTime.UtcNow);
        await runStore.AddAsync(run, cancellationToken);
        run.Start(DateTime.UtcNow);
        run.AppendEvent("attempt-started", $"Manual {request.Schedule} attempt started with seed {request.Seed}.", DateTime.UtcNow);
        await runStore.SaveAsync(run, cancellationToken);

        var plan = CreatePlan(request);
        var attemptId = Guid.NewGuid();
        var attempt = RunAttempt.Start(attemptId, run.Id, request.Schedule.ToString(), request.Seed, DateTime.UtcNow);
        await attemptStore.AddAsync(attempt, CancellationToken.None);
        var traces = new TraceCollector();
        var progressLock = new SemaphoreSlim(1, 1);
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var monitorStop = new CancellationTokenSource();
        var cancellationMonitor = MonitorCancellationAsync(run.Id, executionCancellation, monitorStop.Token);
        ScheduleExecutionResult? execution = null;
        Exception? executionFailure = null;
        try
        {
            execution = await scheduler.ExecuteAsync(plan, request.Budget, async (actor, token) =>
            {
                var result = await targetOperation(actor, token);
                await progressLock.WaitAsync(CancellationToken.None);
                try
                {
                    var trace = traces.Append(
                        run.Id,
                        attemptId,
                        actor.ActorId,
                        "target-operation",
                        result.Succeeded ? "response-success" : "response-failure",
                        result.RequestId ?? $"actor-{actor.ActorId}",
                        DateTime.UtcNow);
                    await traceStore.AppendAsync(trace, CancellationToken.None);
                    run.AppendEvent(
                        "target-call-completed",
                        $"Actor {actor.ActorId} completed with trace sequence {trace.Sequence}.",
                        DateTime.UtcNow);
                    await runStore.SaveAsync(run, CancellationToken.None);
                }
                finally
                {
                    progressLock.Release();
                }
                return result;
            }, executionCancellation.Token);
        }
        catch (Exception exception)
        {
            executionFailure = exception;
        }
        finally
        {
            monitorStop.Cancel();
            var monitorFailure = await cancellationMonitor;
            executionFailure ??= monitorFailure;
        }

        if (executionFailure is not null) return await PersistFailureAsync(run, attempt, executionFailure);
        var completedExecution = execution ?? throw new InvalidOperationException("The scheduler returned no outcome.");

        var persistenceToken = CancellationToken.None;
        if (completedExecution.Cancelled)
        {
            var durable = await runStore.GetAsync(run.Id, persistenceToken);
            if (durable is not null) run = durable;
            if (run.CancellationRequestedAtUtc is null) run.RequestCancellation(DateTime.UtcNow);
            run.AppendEvent("cancellation-observed", "Cancellation observed; no new target work will start.", DateTime.UtcNow);
            run.Cancel(DateTime.UtcNow);
            attempt.Cancel(DateTime.UtcNow);
            await attemptStore.SaveAsync(attempt, persistenceToken);
            await runStore.SaveAsync(run, persistenceToken);
            return new ManualHuntExecutionResult(run, null);
        }

        var observations = completedExecution.Executions.SelectMany(item => item.TargetResult.Observations).ToArray();
        if (completedExecution.StopReason is BudgetStopReason.RequestsExhausted or BudgetStopReason.DurationExhausted)
        {
            run.AppendEvent("budget-exhausted", $"Execution stopped because {completedExecution.StopReason}.", DateTime.UtcNow);
        }
        var invariant = new InvariantEvaluatorRegistry().Evaluate(request.Invariant, observations);
        run.AppendEvent(
            invariant.Outcome switch
            {
                InvariantOutcome.Pass => "invariant-passed",
                InvariantOutcome.Fail => "invariant-failed",
                InvariantOutcome.Inconclusive => "invariant-inconclusive",
                _ => throw new ArgumentOutOfRangeException()
            },
            invariant.Summary,
            DateTime.UtcNow);
        run.Complete(DateTime.UtcNow);
        attempt.Complete(DateTime.UtcNow);
        await attemptStore.SaveAsync(attempt, persistenceToken);
        await runStore.SaveAsync(run, persistenceToken);
        return new ManualHuntExecutionResult(run, invariant.Outcome);
    }

    private static SchedulePlan CreatePlan(ManualHuntRequest request) => request.Schedule switch
    {
        ScheduleKind.SimultaneousStart => new SimultaneousStartStrategy().Create(request.Budget.MaxActors, request.Seed),
        ScheduleKind.SeededJitter => new SeededJitterStrategy(TimeSpan.FromMilliseconds(25)).Create(request.Budget.MaxActors, request.Seed),
        ScheduleKind.CheckpointInterleaving => new CheckpointStrategy().Create(request.Budget.MaxActors, request.Seed),
        _ => throw new ArgumentOutOfRangeException(nameof(request))
    };

    private async Task<Exception?> MonitorCancellationAsync(
        Guid runId,
        CancellationTokenSource executionCancellation,
        CancellationToken stopToken)
    {
        try
        {
            while (!stopToken.IsCancellationRequested && !executionCancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stopToken);
                var requestedAtUtc = await cancellationProbe.GetRequestedAtUtcAsync(runId, stopToken);
                if (requestedAtUtc is not null) executionCancellation.Cancel();
            }
            return null;
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            executionCancellation.Cancel();
            return exception;
        }
    }

    private async Task<ManualHuntExecutionResult> PersistFailureAsync(
        ExperimentRun run,
        RunAttempt attempt,
        Exception exception)
    {
        var durable = await runStore.GetAsync(run.Id, CancellationToken.None);
        if (durable is not null) run = durable;
        if (run.Status == RunStatus.Running)
        {
            run.AppendEvent(
                "execution-failed",
                $"Execution failed with category {exception.GetType().Name}.",
                DateTime.UtcNow);
            run.Fail(DateTime.UtcNow);
        }
        attempt.Fail(DateTime.UtcNow);
        await attemptStore.SaveAsync(attempt, CancellationToken.None);
        await runStore.SaveAsync(run, CancellationToken.None);
        return new ManualHuntExecutionResult(run, null);
    }
}
