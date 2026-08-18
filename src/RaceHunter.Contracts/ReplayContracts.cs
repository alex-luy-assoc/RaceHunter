namespace RaceHunter.Contracts;

public sealed record VerifyFixRequest(string IdempotencyKey);

public sealed record ReplayComparisonResponse(
    string VulnerableOutcome,
    string FixedOutcome,
    string ArtifactFingerprint,
    string IdempotencyKey);

public sealed record WorkerReplayRequest(
    Guid ArtifactId,
    Guid FindingId,
    string ScenarioVersionId,
    string InvariantVersionId,
    string TargetSnapshot,
    string Strategy,
    int Seed,
    IReadOnlyList<ReplayStepResponse> Steps,
    string RequestTemplateJson,
    DateTime CreatedAtUtc,
    string Fingerprint,
    string TargetMode,
    string IdempotencyKey);

public sealed record WorkerReplayResponse(
    Guid Id,
    Guid ArtifactId,
    string TargetMode,
    string Outcome,
    IReadOnlyList<string> TraceReferences,
    string ArtifactFingerprint,
    string IdempotencyKey,
    DateTime CompletedAtUtc);
