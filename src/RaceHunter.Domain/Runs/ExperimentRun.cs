using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Common;

namespace RaceHunter.Domain.Runs;

public enum RunStatus
{
    Queued,
    Running,
    Reproducing,
    Minimizing,
    Completed,
    Failed,
    Cancelled
}

public sealed record RunEvent(long Cursor, string Kind, string Message, DateTime OccurredAtUtc);

public sealed class ExperimentRun
{
    private readonly List<RunEvent> events = [];

    private ExperimentRun(Guid id, ExperimentBudget budget, RunStatus status, DateTime createdAtUtc)
    {
        if (id == Guid.Empty) throw new DomainException("A run ID is required.");
        Id = id;
        Budget = budget;
        Status = status;
        CreatedAtUtc = EnsureUtc(createdAtUtc);
    }

    public Guid Id { get; }
    public ExperimentBudget Budget { get; }
    public RunStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancellationRequestedAtUtc { get; private set; }
    public IReadOnlyList<RunEvent> Events => events;
    public bool IsActive => Status is RunStatus.Running or RunStatus.Reproducing or RunStatus.Minimizing;

    public static ExperimentRun Queue(Guid id, ExperimentBudget budget, DateTime createdAtUtc) =>
        new(id, budget, RunStatus.Queued, createdAtUtc);

    public static ExperimentRun Rehydrate(
        Guid id,
        ExperimentBudget budget,
        RunStatus status,
        DateTime createdAtUtc,
        DateTime? startedAtUtc,
        DateTime? completedAtUtc,
        DateTime? cancellationRequestedAtUtc,
        IEnumerable<RunEvent> persistedEvents)
    {
        var run = new ExperimentRun(id, budget, status, createdAtUtc)
        {
            StartedAtUtc = EnsureNullableUtc(startedAtUtc),
            CompletedAtUtc = EnsureNullableUtc(completedAtUtc),
            CancellationRequestedAtUtc = EnsureNullableUtc(cancellationRequestedAtUtc)
        };
        run.events.AddRange(persistedEvents.OrderBy(item => item.Cursor));
        return run;
    }

    public void Start(DateTime nowUtc)
    {
        RequireStatus(RunStatus.Queued);
        Status = RunStatus.Running;
        StartedAtUtc = EnsureUtc(nowUtc);
    }

    public RunEvent AppendEvent(string kind, string message, DateTime occurredAtUtc)
    {
        if (Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
            throw new DomainException("Terminal runs cannot receive progress events.");
        if (string.IsNullOrWhiteSpace(kind)) throw new DomainException("An event kind is required.");
        var item = new RunEvent(events.Count == 0 ? 1 : events[^1].Cursor + 1, kind.Trim(), message.Trim(), EnsureUtc(occurredAtUtc));
        events.Add(item);
        return item;
    }

    public bool BeginReproduction(DateTime nowUtc)
    {
        if (Status is RunStatus.Reproducing or RunStatus.Minimizing) return false;
        RequireStatus(RunStatus.Running);
        Status = RunStatus.Reproducing;
        AppendEvent("reproduction-started", "Measuring the deterministic violation across three persisted reproductions.", nowUtc);
        return true;
    }

    public bool BeginMinimization(DateTime nowUtc)
    {
        if (Status == RunStatus.Minimizing) return false;
        RequireStatus(RunStatus.Reproducing);
        Status = RunStatus.Minimizing;
        AppendEvent("minimization-started", "Reducing the verified failing schedule while preserving deterministic evidence.", nowUtc);
        return true;
    }

    public bool RequestCancellation(DateTime nowUtc)
    {
        if (Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled || CancellationRequestedAtUtc.HasValue) return false;
        CancellationRequestedAtUtc = EnsureUtc(nowUtc);
        return true;
    }

    public void Complete(DateTime nowUtc)
    {
        RequireActive();
        Status = RunStatus.Completed;
        CompletedAtUtc = EnsureUtc(nowUtc);
    }

    public void Cancel(DateTime nowUtc)
    {
        RequireActive();
        Status = RunStatus.Cancelled;
        CompletedAtUtc = EnsureUtc(nowUtc);
    }

    public void Fail(DateTime nowUtc)
    {
        RequireActive();
        Status = RunStatus.Failed;
        CompletedAtUtc = EnsureUtc(nowUtc);
    }

    private void RequireStatus(RunStatus expected)
    {
        if (Status != expected) throw new DomainException($"Run must be {expected} but is {Status}.");
    }

    private void RequireActive()
    {
        if (!IsActive) throw new DomainException($"Run must be active but is {Status}.");
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? EnsureNullableUtc(DateTime? value) => value.HasValue ? EnsureUtc(value.Value) : null;
}
