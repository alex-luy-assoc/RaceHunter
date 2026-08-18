namespace RaceHunter.Application.Messaging;

public enum WorkKind
{
    Unknown,
    PlanRequested,
    RunRequested
}

public sealed record WorkDispatch(
    string Version,
    Guid WorkId,
    WorkKind Kind,
    Guid SubjectId,
    string CorrelationId,
    DateTime CreatedAtUtc);

public enum WorkAcquireOutcome
{
    Acquired,
    Duplicate,
    Busy,
    RetryLater,
    Resumed
}

public sealed record WorkCheckpoint(string Boundary, int Iteration, string StateJson, DateTime PersistedAtUtc);
public sealed record WorkAcquireResult(WorkAcquireOutcome Outcome, int DeliveryAttempt, WorkCheckpoint? Checkpoint);

public enum WorkFailureCategory
{
    Target,
    Transport,
    Model,
    Persistence,
    Orchestration,
    Cancellation,
    Validation,
    SafetyAuthorization,
    Poison
}

public sealed record WorkFailure(
    WorkFailureCategory Category,
    bool Transient,
    bool OperationIsIdempotent,
    string SanitizedDiagnostic);

public enum WorkFailureOutcome
{
    RetryScheduled,
    DeadLettered
}

public interface IWorkInbox
{
    Task<WorkAcquireResult> TryAcquireAsync(Guid workId, string messageId, string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<bool> HeartbeatAsync(Guid workId, string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task SaveCheckpointAsync(Guid workId, string owner, WorkCheckpoint checkpoint, CancellationToken cancellationToken);
    Task CompleteAsync(Guid workId, string owner, DateTime nowUtc, CancellationToken cancellationToken);
    Task<WorkFailureOutcome> RecordFailureAsync(Guid workId, string owner, WorkFailure failure, int maxRetries, DateTime nowUtc, CancellationToken cancellationToken);
}

public static class WorkRetryPolicy
{
    public static TimeSpan Delay(int deliveryAttempt, Guid workId)
    {
        var exponent = Math.Clamp(deliveryAttempt - 1, 0, 5);
        var baseMilliseconds = 500 * (1 << exponent);
        var bytes = workId.ToByteArray();
        var jitterMilliseconds = (bytes[0] << 8 | bytes[1]) % 251;
        return TimeSpan.FromMilliseconds(Math.Min(30_000, baseMilliseconds + jitterMilliseconds));
    }
}

public interface IWorkPublisher
{
    Task PublishAsync(WorkDispatch message, CancellationToken cancellationToken);
    Task PublishDeadLetterAsync(WorkDispatch message, WorkFailure failure, CancellationToken cancellationToken);
}
