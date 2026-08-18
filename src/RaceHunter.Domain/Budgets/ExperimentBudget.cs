using RaceHunter.Domain.Common;

namespace RaceHunter.Domain.Budgets;

public sealed record ExperimentBudget
{
    public ExperimentBudget(
        int maxActors,
        int maxConcurrentActors,
        int maxRequests,
        int maxModelCalls,
        TimeSpan maxDuration,
        int maxRetries)
    {
        if (maxActors is < 1 or > 100) throw new DomainException("Actor budget must be between 1 and 100.");
        if (maxConcurrentActors is < 1 || maxConcurrentActors > maxActors)
            throw new DomainException("Experiment concurrency must be between 1 and the actor budget.");
        if (maxRequests < 1) throw new DomainException("Request budget must be positive.");
        if (maxModelCalls < 0) throw new DomainException("Model-call budget cannot be negative.");
        if (maxDuration <= TimeSpan.Zero) throw new DomainException("Duration budget must be positive.");
        if (maxRetries < 0) throw new DomainException("Retry budget cannot be negative.");

        MaxActors = maxActors;
        MaxConcurrentActors = maxConcurrentActors;
        MaxRequests = maxRequests;
        MaxModelCalls = maxModelCalls;
        MaxDuration = maxDuration;
        MaxRetries = maxRetries;
    }

    public int MaxActors { get; }
    public int MaxConcurrentActors { get; }
    public int MaxRequests { get; }
    public int MaxModelCalls { get; }
    public TimeSpan MaxDuration { get; }
    public int MaxRetries { get; }

    public static ExperimentBudget PublicSandbox { get; } =
        new(10, 10, 40, 5, TimeSpan.FromSeconds(90), 1);
}

public enum BudgetStopReason
{
    None,
    RequestsExhausted,
    DurationExhausted,
    Cancelled
}

public sealed class BudgetLedger(ExperimentBudget budget, DateTime startedAtUtc)
{
    private int requestsConsumed;
    private BudgetStopReason stopReason;

    public int RequestsConsumed => Volatile.Read(ref requestsConsumed);
    public BudgetStopReason StopReason
    {
        get { lock (this) return stopReason; }
    }

    public bool TryConsumeRequest(DateTime nowUtc)
    {
        lock (this)
        {
            if (stopReason != BudgetStopReason.None) return false;
            if (nowUtc - startedAtUtc >= budget.MaxDuration)
            {
                stopReason = BudgetStopReason.DurationExhausted;
                return false;
            }

            if (requestsConsumed >= budget.MaxRequests)
            {
                stopReason = BudgetStopReason.RequestsExhausted;
                return false;
            }

            requestsConsumed++;
            return true;
        }
    }

    public void MarkCancelled()
    {
        lock (this) stopReason = BudgetStopReason.Cancelled;
    }

    public void MarkDurationExhausted()
    {
        lock (this)
        {
            if (stopReason == BudgetStopReason.None) stopReason = BudgetStopReason.DurationExhausted;
        }
    }
}
