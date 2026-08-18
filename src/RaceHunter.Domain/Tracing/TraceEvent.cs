namespace RaceHunter.Domain.Tracing;

public sealed record TraceEvent(
    long Sequence,
    Guid RunId,
    Guid AttemptId,
    int ActorId,
    string StepId,
    string Kind,
    string RequestId,
    DateTime OccurredAtUtc);
