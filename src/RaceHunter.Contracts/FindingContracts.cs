namespace RaceHunter.Contracts;

public sealed record ReproductionResponse(int Attempt, string Outcome, IReadOnlyList<string> TraceReferences);
public sealed record ReplayStepResponse(int ActorId, string StepId, string OperationId, int OffsetMilliseconds);
public sealed record ReplayArtifactResponse(
    Guid Id,
    string Fingerprint,
    string Strategy,
    int Seed,
    int ActorCount,
    int StepCount,
    IReadOnlyList<ReplayStepResponse> Steps);
public sealed record TimelineEventResponse(long Sequence, Guid AttemptId, string StepId, string Kind, string RequestId, DateTime OccurredAtUtc);
public sealed record ActorLaneResponse(int ActorId, IReadOnlyList<TimelineEventResponse> Events);
public sealed record AgentActivityResponse(
    int Iteration,
    string Action,
    string RationaleSummary,
    string ModelId,
    string SchemaVersion,
    string ModelInvocationId,
    DateTime OccurredAtUtc);
public sealed record ReplayAttemptResponse(
    Guid Id,
    string TargetMode,
    string Outcome,
    string ArtifactFingerprint,
    string IdempotencyKey,
    DateTime CompletedAtUtc);
public sealed record FindingResponse(
    Guid Id,
    Guid RunId,
    string SuccessMessage,
    string InvariantOutcome,
    string InvariantSummary,
    IReadOnlyList<string> TraceReferences,
    string AgentInterpretation,
    IReadOnlyList<ReproductionResponse> Reproductions,
    ReplayArtifactResponse ReplayArtifact,
    IReadOnlyList<ActorLaneResponse> Timeline,
    IReadOnlyList<AgentActivityResponse> AgentActivity,
    IReadOnlyList<ReplayAttemptResponse> ReplayAttempts);
