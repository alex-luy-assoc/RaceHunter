using RaceHunter.Domain.Common;

namespace RaceHunter.Domain.Runs;

public enum RunAttemptStatus
{
    Running,
    Completed,
    Cancelled,
    Failed
}

public sealed class RunAttempt
{
    private RunAttempt(Guid id, Guid runId, string strategy, int seed, RunAttemptStatus status, DateTime startedAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty) throw new DomainException("Attempt and run IDs are required.");
        if (string.IsNullOrWhiteSpace(strategy)) throw new DomainException("A scheduling strategy is required.");
        Id = id;
        RunId = runId;
        Strategy = strategy.Trim();
        Seed = seed;
        Status = status;
        StartedAtUtc = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; }
    public Guid RunId { get; }
    public string Strategy { get; }
    public int Seed { get; }
    public RunAttemptStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static RunAttempt Start(Guid id, Guid runId, string strategy, int seed, DateTime startedAtUtc) =>
        new(id, runId, strategy, seed, RunAttemptStatus.Running, startedAtUtc);

    public void Complete(DateTime nowUtc) => Finish(RunAttemptStatus.Completed, nowUtc);
    public void Cancel(DateTime nowUtc) => Finish(RunAttemptStatus.Cancelled, nowUtc);
    public void Fail(DateTime nowUtc) => Finish(RunAttemptStatus.Failed, nowUtc);

    private void Finish(RunAttemptStatus status, DateTime nowUtc)
    {
        if (Status != RunAttemptStatus.Running) throw new DomainException("Only a running attempt can finish.");
        Status = status;
        CompletedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
    }
}
