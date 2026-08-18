using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RaceHunter.Domain.Invariants;

namespace RaceHunter.Domain.Replays;

public enum ReplayTargetMode
{
    Vulnerable,
    Fixed
}

public sealed record ReplayStep(int ActorId, string StepId, string OperationId, int OffsetMilliseconds);

public sealed class ReplayArtifact
{
    private ReplayArtifact(
        Guid id,
        Guid findingId,
        string scenarioVersionId,
        string invariantVersionId,
        string targetSnapshot,
        string strategy,
        int seed,
        IEnumerable<ReplayStep> steps,
        string requestTemplateJson,
        DateTime createdAtUtc,
        string? expectedFingerprint)
    {
        if (id == Guid.Empty || findingId == Guid.Empty) throw new ArgumentException("Replay and finding IDs are required.");
        Id = id;
        FindingId = findingId;
        ScenarioVersionId = Required(scenarioVersionId, "A scenario version is required.");
        InvariantVersionId = Required(invariantVersionId, "An invariant version is required.");
        TargetSnapshot = Required(targetSnapshot, "A target snapshot is required.");
        Strategy = Required(strategy, "A replay strategy is required.");
        Seed = seed;
        Steps = Array.AsReadOnly(steps.OrderBy(item => item.ActorId).ThenBy(item => item.OffsetMilliseconds).ThenBy(item => item.StepId, StringComparer.Ordinal).ToArray());
        if (Steps.Count == 0 || Steps.Any(item => item.ActorId < 1 || string.IsNullOrWhiteSpace(item.StepId) || string.IsNullOrWhiteSpace(item.OperationId) || item.OffsetMilliseconds < 0))
            throw new ArgumentException("Replay steps must identify a positive actor, operation, step, and non-negative offset.");
        RequestTemplateJson = CanonicalJson(requestTemplateJson);
        CreatedAtUtc = EnsureUtc(createdAtUtc);
        Fingerprint = ComputeFingerprint(this);
        if (expectedFingerprint is not null && !FingerprintsMatch(Fingerprint, expectedFingerprint))
            throw new InvalidDataException("The replay artifact content does not match its immutable fingerprint.");
    }

    public Guid Id { get; }
    public Guid FindingId { get; }
    public string ScenarioVersionId { get; }
    public string InvariantVersionId { get; }
    public string TargetSnapshot { get; }
    public string Strategy { get; }
    public int Seed { get; }
    public IReadOnlyList<ReplayStep> Steps { get; }
    public string RequestTemplateJson { get; }
    public DateTime CreatedAtUtc { get; }
    public string Fingerprint { get; }
    public int ActorCount => Steps.Select(item => item.ActorId).Distinct().Count();

    public static ReplayArtifact Create(
        Guid id,
        Guid findingId,
        string scenarioVersionId,
        string invariantVersionId,
        string targetSnapshot,
        string strategy,
        int seed,
        IEnumerable<ReplayStep> steps,
        string requestTemplateJson,
        DateTime createdAtUtc) =>
        new(id, findingId, scenarioVersionId, invariantVersionId, targetSnapshot, strategy, seed, steps, requestTemplateJson, createdAtUtc, null);

    public static ReplayArtifact Rehydrate(
        Guid id,
        Guid findingId,
        string scenarioVersionId,
        string invariantVersionId,
        string targetSnapshot,
        string strategy,
        int seed,
        IEnumerable<ReplayStep> steps,
        string requestTemplateJson,
        DateTime createdAtUtc,
        string fingerprint) =>
        new(id, findingId, scenarioVersionId, invariantVersionId, targetSnapshot, strategy, seed, steps, requestTemplateJson, createdAtUtc, fingerprint);

    public void VerifyIntegrity()
    {
        if (!FingerprintsMatch(Fingerprint, ComputeFingerprint(this)))
            throw new InvalidDataException("The replay artifact content does not match its immutable fingerprint.");
    }

    private static string ComputeFingerprint(ReplayArtifact artifact)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            artifact.Id,
            artifact.FindingId,
            artifact.ScenarioVersionId,
            artifact.InvariantVersionId,
            artifact.TargetSnapshot,
            artifact.Strategy,
            artifact.Seed,
            Steps = artifact.Steps.Select(item => new
            {
                item.ActorId,
                item.StepId,
                item.OperationId,
                item.OffsetMilliseconds
            }).ToArray(),
            artifact.RequestTemplateJson,
            CreatedAtUtc = artifact.CreatedAtUtc.ToString("O")
        });
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }

    private static bool FingerprintsMatch(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string Required(string value, string message) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();
    private static string CanonicalJson(string value)
    {
        value = Required(value, "A request template is required.");
        using var document = JsonDocument.Parse(value);
        return JsonSerializer.Serialize(document.RootElement);
    }
    private static DateTime EnsureUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}

public sealed class ReplayAttempt
{
    private ReplayAttempt(Guid id, Guid artifactId, ReplayTargetMode targetMode, InvariantOutcome outcome,
        IEnumerable<string> traceReferences, string artifactFingerprint, string idempotencyKey, DateTime completedAtUtc)
    {
        if (id == Guid.Empty || artifactId == Guid.Empty) throw new ArgumentException("Replay attempt and artifact IDs are required.");
        Id = id;
        ArtifactId = artifactId;
        TargetMode = targetMode;
        Outcome = outcome;
        TraceReferences = traceReferences.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        ArtifactFingerprint = Required(artifactFingerprint, "An artifact fingerprint is required.");
        IdempotencyKey = Required(idempotencyKey, "An idempotency key is required.");
        CompletedAtUtc = EnsureUtc(completedAtUtc);
    }

    public Guid Id { get; }
    public Guid ArtifactId { get; }
    public ReplayTargetMode TargetMode { get; }
    public InvariantOutcome Outcome { get; }
    public IReadOnlyList<string> TraceReferences { get; }
    public string ArtifactFingerprint { get; }
    public string IdempotencyKey { get; }
    public DateTime CompletedAtUtc { get; }

    public static ReplayAttempt Complete(Guid id, Guid artifactId, ReplayTargetMode targetMode, InvariantOutcome outcome,
        IEnumerable<string> traceReferences, string artifactFingerprint, string idempotencyKey, DateTime completedAtUtc) =>
        new(id, artifactId, targetMode, outcome, traceReferences, artifactFingerprint, idempotencyKey, completedAtUtc);

    private static string Required(string value, string message) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();
    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
