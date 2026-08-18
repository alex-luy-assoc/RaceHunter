namespace RaceHunter.Contracts;

public sealed record CreateHuntRequest(
    string Objective,
    int MaxActors = 10,
    int MaxConcurrency = 10,
    int MaxRequests = 40,
    int MaxModelCalls = 5,
    int MaxDurationSeconds = 90,
    int MaxRetries = 1);

public sealed record HuntResponse(Guid Id, string Objective, string Status, DateTime CreatedAtUtc);
public sealed record PlanResponse(
    string PlanVersion,
    string SchemaVersion,
    string PromptVersion,
    string ModelId,
    IReadOnlyList<PlanActorResponse> Actors,
    PlanInvariantResponse Invariant,
    PlanStrategyResponse Strategy);
public sealed record PlanActorResponse(string Name, string OperationId);
public sealed record PlanInvariantResponse(
    string Type,
    string Metric,
    decimal? Maximum,
    string? LeftMetric,
    string? RightMetric,
    string? Relation);
public sealed record PlanStrategyResponse(string Kind, int ActorCount, int Seed);
public sealed record ApproveRunRequest(string PlanVersion, string IdempotencyKey);
public sealed record ApprovalResponse(Guid RunId, string PlanVersion);
