using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Domain.Findings;

public sealed record ReproductionAttempt(int Attempt, InvariantOutcome Outcome, IReadOnlyList<string> TraceReferences);

public sealed class Finding
{
    private Finding(Guid id, Guid runId, string invariantVersionId, InvariantResult originalInvariant,
        IEnumerable<ReproductionAttempt> reproductions, ReplayArtifact artifact, DateTime createdAtUtc, string agentInterpretation)
    {
        if (id == Guid.Empty || runId == Guid.Empty) throw new ArgumentException("Finding and run IDs are required.");
        if (originalInvariant.Outcome != InvariantOutcome.Fail)
            throw new InvalidOperationException("Only a deterministic failed invariant can create a finding.");
        var measured = reproductions.OrderBy(item => item.Attempt).ToArray();
        if (measured.Length != 3 || measured.Select(item => item.Attempt).Distinct().Count() != 3 || measured.Any(item => item.Outcome != InvariantOutcome.Fail))
            throw new InvalidOperationException("A reference finding requires a measured three failures out of three reproductions.");
        if (artifact.FindingId != id || artifact.ActorCount != 2)
            throw new InvalidOperationException("A reference finding requires its own replay artifact minimized to two actors.");
        Id = id;
        RunId = runId;
        InvariantVersionId = string.IsNullOrWhiteSpace(invariantVersionId) ? throw new ArgumentException("An invariant version is required.") : invariantVersionId.Trim();
        OriginalInvariant = originalInvariant with { TraceReferences = originalInvariant.TraceReferences.ToArray() };
        Reproductions = measured.Select(item => item with { TraceReferences = item.TraceReferences.ToArray() }).ToArray();
        ReplayArtifactId = artifact.Id;
        CreatedAtUtc = createdAtUtc.Kind == DateTimeKind.Utc ? createdAtUtc : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        AgentInterpretation = agentInterpretation.Trim();
    }

    public Guid Id { get; }
    public Guid RunId { get; }
    public string InvariantVersionId { get; }
    public InvariantResult OriginalInvariant { get; }
    public IReadOnlyList<ReproductionAttempt> Reproductions { get; }
    public Guid ReplayArtifactId { get; }
    public DateTime CreatedAtUtc { get; }
    public string AgentInterpretation { get; }

    public static Finding CreateReference(Guid id, Guid runId, string invariantVersionId, InvariantResult originalInvariant,
        IEnumerable<ReproductionAttempt> reproductions, ReplayArtifact artifact, DateTime createdAtUtc, string agentInterpretation) =>
        new(id, runId, invariantVersionId, originalInvariant, reproductions, artifact, createdAtUtc, agentInterpretation);
}
