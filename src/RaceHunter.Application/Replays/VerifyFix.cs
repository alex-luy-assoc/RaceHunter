using RaceHunter.Application.Abstractions;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Application.Replays;

public interface IReplayExecution
{
    Task<ReplayAttempt> ExecuteAsync(
        ReplayArtifact artifact,
        ReplayTargetMode targetMode,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class VerifyFix(IFindingStore findings, IReplayStore replays, IReplayExecution execution)
{
    public async Task<ReplayAttempt> ExecuteAsync(Guid findingId, string idempotencyKey, CancellationToken cancellationToken)
    {
        idempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        var finding = await findings.GetAsync(findingId, cancellationToken)
            ?? throw new KeyNotFoundException("The finding does not exist.");
        var artifact = await replays.GetArtifactAsync(finding.ReplayArtifactId, cancellationToken)
            ?? throw new InvalidOperationException("The finding's immutable replay artifact is missing.");
        var expectedFingerprint = artifact.Fingerprint;
        return await replays.ExecuteFixedOnceAsync(artifact.Id, async token =>
        {
            var attempt = await execution.ExecuteAsync(artifact, ReplayTargetMode.Fixed, idempotencyKey, token);
            artifact.VerifyIntegrity();
            if (!string.Equals(expectedFingerprint, attempt.ArtifactFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Verify Fix must replay the original immutable artifact without mutation.");
            return attempt;
        }, cancellationToken);
    }

    public static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        var normalized = idempotencyKey?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 160)
            throw new ArgumentException("The idempotency key must contain between 1 and 160 characters.", nameof(idempotencyKey));
        return normalized;
    }
}
