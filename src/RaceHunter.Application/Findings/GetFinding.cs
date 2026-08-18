using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Hunts;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Application.Findings;

public sealed record ReproductionProjection(int Attempt, InvariantOutcome Outcome, IReadOnlyList<string> TraceReferences);
public sealed record ReplayStepProjection(int ActorId, string StepId, string OperationId, int OffsetMilliseconds);
public sealed record ReplayArtifactProjection(
    Guid Id,
    string Fingerprint,
    string Strategy,
    int Seed,
    int ActorCount,
    int StepCount,
    IReadOnlyList<ReplayStepProjection> Steps);
public sealed record TimelineEventProjection(long Sequence, Guid AttemptId, string StepId, string Kind, string RequestId, DateTime OccurredAtUtc);
public sealed record ActorLaneProjection(int ActorId, IReadOnlyList<TimelineEventProjection> Events);
public sealed record AgentActivityProjection(
    int Iteration,
    string Action,
    string RationaleSummary,
    string ModelId,
    string SchemaVersion,
    string ModelInvocationId,
    DateTime OccurredAtUtc);
public sealed record ReplayAttemptProjection(
    Guid Id,
    string TargetMode,
    string Outcome,
    string ArtifactFingerprint,
    string IdempotencyKey,
    DateTime CompletedAtUtc);
public sealed record FindingProjection(
    Guid Id,
    Guid RunId,
    string SuccessMessage,
    InvariantOutcome InvariantOutcome,
    string InvariantSummary,
    IReadOnlyList<string> TraceReferences,
    string AgentInterpretation,
    IReadOnlyList<ReproductionProjection> Reproductions,
    ReplayArtifactProjection ReplayArtifact,
    IReadOnlyList<ActorLaneProjection> Timeline,
    IReadOnlyList<AgentActivityProjection> AgentActivity,
    IReadOnlyList<ReplayAttemptProjection> ReplayAttempts);

public sealed class GetFinding(
    IFindingStore findings,
    IReplayStore replays,
    IAgentIterationReader agentIterations,
    ITraceStore traces)
{
    public const string VerifiedReferenceMessage = "Race condition verified — reproduced 3/3 and minimized to 2 actors.";

    public async Task<FindingProjection?> ExecuteAsync(Guid findingId, CancellationToken cancellationToken)
    {
        var finding = await findings.GetAsync(findingId, cancellationToken);
        if (finding is null) return null;
        var artifact = await replays.GetArtifactAsync(finding.ReplayArtifactId, cancellationToken)
            ?? throw new InvalidOperationException("The finding's immutable replay artifact is missing.");
        var referencedSequences = finding.OriginalInvariant.TraceReferences
            .Select(ParseTraceSequence)
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToHashSet();
        var traceEvents = (await traces.GetAsync(finding.RunId, 0, cancellationToken))
            .Where(item => referencedSequences.Contains(item.Sequence))
            .ToArray();
        var timeline = traceEvents
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Sequence)
            .GroupBy(item => item.ActorId)
            .OrderBy(group => group.Key)
            .Select(group => new ActorLaneProjection(
                group.Key,
                group.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Sequence)
                    .Select(item => new TimelineEventProjection(item.Sequence, item.AttemptId, item.StepId, item.Kind, item.RequestId, item.OccurredAtUtc))
                    .ToArray()))
            .ToArray();
        var activity = await agentIterations.GetIterationsByRunAsync(finding.RunId, cancellationToken);
        var attempts = await replays.GetAttemptsAsync(artifact.Id, cancellationToken);
        return new FindingProjection(
            finding.Id,
            finding.RunId,
            VerifiedReferenceMessage,
            finding.OriginalInvariant.Outcome,
            finding.OriginalInvariant.Summary,
            finding.OriginalInvariant.TraceReferences,
            finding.AgentInterpretation,
            finding.Reproductions.Select(item => new ReproductionProjection(item.Attempt, item.Outcome, item.TraceReferences)).ToArray(),
            new ReplayArtifactProjection(
                artifact.Id,
                artifact.Fingerprint,
                artifact.Strategy,
                artifact.Seed,
                artifact.ActorCount,
                artifact.Steps.Count,
                artifact.Steps.Select(item => new ReplayStepProjection(item.ActorId, item.StepId, item.OperationId, item.OffsetMilliseconds)).ToArray()),
            timeline,
            activity.OrderBy(item => item.Iteration).Select(item => new AgentActivityProjection(
                item.Iteration, item.Action, item.RationaleSummary, item.ModelId, item.SchemaVersion, item.ModelInvocationId, item.OccurredAtUtc)).ToArray(),
            attempts.OrderBy(item => item.CompletedAtUtc).Select(ToProjection).ToArray());
    }

    internal static ReplayAttemptProjection ToProjection(ReplayAttempt attempt) => new(
        attempt.Id,
        attempt.TargetMode.ToString(),
        attempt.Outcome.ToString(),
        attempt.ArtifactFingerprint,
        attempt.IdempotencyKey,
        attempt.CompletedAtUtc);

    private static long? ParseTraceSequence(string reference) =>
        reference.StartsWith("trace:", StringComparison.Ordinal) &&
        long.TryParse(reference.AsSpan("trace:".Length), out var sequence)
            ? sequence
            : null;
}
