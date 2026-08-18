namespace RaceHunter.Contracts;

public sealed record CreateHuntRequest(
    string Objective,
    int MaxActors = 10,
    int MaxConcurrency = 10,
    int MaxRequests = 40,
    int MaxModelCalls = 5,
    int MaxDurationSeconds = 90,
    int MaxRetries = 1,
    Guid? TargetId = null);

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

public sealed record ConfigureManualTargetRequest(
    string BaseUrl,
    IReadOnlyList<string> AllowedHosts,
    bool AuthorizationAcknowledged,
    string CredentialReference,
    IReadOnlyList<ManualTargetOperationRequest> Operations,
    IReadOnlyList<string> SensitiveJsonPaths);

public sealed record ManualTargetOperationRequest(
    string Id,
    string Method,
    string Path,
    string RequestTemplateJson,
    IReadOnlyDictionary<string, string> ObservationPaths,
    bool IsSetup = false,
    IReadOnlyDictionary<string, string>? ObservationTypes = null);

public sealed record ManualTargetResponse(
    Guid Id,
    string BaseUrl,
    string Host,
    string CredentialReference,
    IReadOnlyList<ManualTargetOperationRequest> Operations,
    IReadOnlyList<string> SensitiveJsonPaths,
    DateTime CreatedAtUtc);

public sealed record CloudProofResponse(
    string ApiRevision,
    string WorkerService,
    string PubSubTopic,
    string CloudSqlInstance,
    string ModelId,
    string SchemaVersions,
    string WorkerAuthentication,
    Guid RunId,
    string RunStatus,
    string PlanVersion,
    string WorkerExecution,
    string ModelInvocationId,
    int TraceEventCount,
    Guid? FindingId,
    string EvidenceCorrelationId,
    string RequestTraceId);
