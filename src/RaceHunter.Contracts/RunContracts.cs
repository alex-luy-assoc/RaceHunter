namespace RaceHunter.Contracts;

public sealed record RunResponse(
    Guid Id,
    string Status,
    int MaxActors,
    int MaxConcurrentActors,
    int MaxRequests,
    int MaxModelCalls,
    int MaxDurationSeconds,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancellationRequestedAtUtc);

public sealed record RunEventResponse(long Cursor, string Kind, string Message, DateTime OccurredAtUtc);

public sealed record TraceEventResponse(
    long Sequence,
    Guid AttemptId,
    int ActorId,
    string StepId,
    string Kind,
    string RequestId,
    DateTime OccurredAtUtc);

public sealed record ManualInventoryHuntRequest(
    Guid? RunId,
    int ActorCount,
    int MaxConcurrency,
    int MaxRequests,
    int MaxDurationSeconds,
    string Schedule,
    int Seed,
    decimal MaximumSuccessfulOrders);

public sealed record ManualInventoryHuntResponse(Guid RunId, string Status, string? InvariantOutcome);
