using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Hunts;
using RaceHunter.Domain.Findings;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class FindingStore(RaceHunterDbContext context) : IFindingStore, IReplayStore, IAgentIterationReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AddAsync(Finding finding, CancellationToken cancellationToken)
    {
        context.Findings.Add(new FindingRecord
        {
            Id = finding.Id,
            RunId = finding.RunId,
            InvariantVersionId = finding.InvariantVersionId,
            InvariantOutcome = finding.OriginalInvariant.Outcome.ToString(),
            InvariantSummary = finding.OriginalInvariant.Summary,
            TraceReferencesJson = JsonSerializer.Serialize(finding.OriginalInvariant.TraceReferences, JsonOptions),
            ReplayArtifactId = finding.ReplayArtifactId,
            AgentInterpretation = finding.AgentInterpretation,
            CreatedAtUtc = finding.CreatedAtUtc,
            Reproductions = finding.Reproductions.Select(item => new FindingReproductionRecord
            {
                FindingId = finding.Id,
                Attempt = item.Attempt,
                Outcome = item.Outcome.ToString(),
                TraceReferencesJson = JsonSerializer.Serialize(item.TraceReferences, JsonOptions)
            }).ToList()
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddVerifiedAsync(Finding finding, ReplayArtifact artifact, ReplayAttempt vulnerableAttempt, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await AddArtifactAsync(artifact, cancellationToken);
        await AddAsync(finding, cancellationToken);
        await AddAttemptAsync(vulnerableAttempt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Finding?> GetAsync(Guid findingId, CancellationToken cancellationToken)
    {
        var item = await context.Findings.AsNoTracking().Include(value => value.Reproductions)
            .SingleOrDefaultAsync(value => value.Id == findingId, cancellationToken);
        if (item is null) return null;
        var artifact = await GetArtifactAsync(item.ReplayArtifactId, cancellationToken)
            ?? throw new InvalidOperationException("The persisted finding references a missing replay artifact.");
        return Finding.CreateReference(
            item.Id,
            item.RunId,
            item.InvariantVersionId,
            new InvariantResult(
                Enum.Parse<InvariantOutcome>(item.InvariantOutcome),
                DeserializeReferences(item.TraceReferencesJson),
                item.InvariantSummary),
            item.Reproductions.OrderBy(value => value.Attempt).Select(value => new ReproductionAttempt(
                value.Attempt,
                Enum.Parse<InvariantOutcome>(value.Outcome),
                DeserializeReferences(value.TraceReferencesJson))).ToArray(),
            artifact,
            item.CreatedAtUtc,
            item.AgentInterpretation);
    }

    public Task<Guid?> GetIdByRunAsync(Guid runId, CancellationToken cancellationToken) =>
        context.Findings.AsNoTracking().Where(item => item.RunId == runId)
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task AddArtifactAsync(ReplayArtifact artifact, CancellationToken cancellationToken)
    {
        context.ReplayArtifacts.Add(new ReplayArtifactRecord
        {
            Id = artifact.Id,
            FindingId = artifact.FindingId,
            ScenarioVersionId = artifact.ScenarioVersionId,
            InvariantVersionId = artifact.InvariantVersionId,
            TargetSnapshot = artifact.TargetSnapshot,
            Strategy = artifact.Strategy,
            Seed = artifact.Seed,
            RequestTemplateJson = artifact.RequestTemplateJson,
            Fingerprint = artifact.Fingerprint,
            CreatedAtUtc = artifact.CreatedAtUtc,
            Steps = artifact.Steps.Select((item, position) => new ReplayStepRecord
            {
                ArtifactId = artifact.Id,
                Position = position,
                ActorId = item.ActorId,
                StepId = item.StepId,
                OperationId = item.OperationId,
                OffsetMilliseconds = item.OffsetMilliseconds
            }).ToList()
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReplayArtifact?> GetArtifactAsync(Guid artifactId, CancellationToken cancellationToken)
    {
        var item = await context.ReplayArtifacts.AsNoTracking().Include(value => value.Steps)
            .SingleOrDefaultAsync(value => value.Id == artifactId, cancellationToken);
        return item is null ? null : ReplayArtifact.Rehydrate(
            item.Id,
            item.FindingId,
            item.ScenarioVersionId,
            item.InvariantVersionId,
            item.TargetSnapshot,
            item.Strategy,
            item.Seed,
            item.Steps.OrderBy(value => value.Position).Select(value => new ReplayStep(
                value.ActorId, value.StepId, value.OperationId, value.OffsetMilliseconds)).ToArray(),
            item.RequestTemplateJson,
            item.CreatedAtUtc,
            item.Fingerprint);
    }

    public async Task AddAttemptAsync(ReplayAttempt attempt, CancellationToken cancellationToken)
    {
        context.ReplayAttempts.Add(new ReplayAttemptRecord
        {
            Id = attempt.Id,
            ArtifactId = attempt.ArtifactId,
            TargetMode = attempt.TargetMode.ToString(),
            Outcome = attempt.Outcome.ToString(),
            TraceReferencesJson = JsonSerializer.Serialize(attempt.TraceReferences, JsonOptions),
            ArtifactFingerprint = attempt.ArtifactFingerprint,
            IdempotencyKey = attempt.IdempotencyKey,
            CompletedAtUtc = attempt.CompletedAtUtc
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReplayAttempt>> GetAttemptsAsync(Guid artifactId, CancellationToken cancellationToken)
    {
        var items = await context.ReplayAttempts.AsNoTracking().Where(item => item.ArtifactId == artifactId)
            .OrderBy(item => item.CompletedAtUtc)
            .ToArrayAsync(cancellationToken);
        return items.Select(item => ReplayAttempt.Complete(
                item.Id,
                item.ArtifactId,
                Enum.Parse<ReplayTargetMode>(item.TargetMode),
                Enum.Parse<InvariantOutcome>(item.Outcome),
                DeserializeReferences(item.TraceReferencesJson),
                item.ArtifactFingerprint,
                item.IdempotencyKey,
                item.CompletedAtUtc))
            .ToArray();
    }

    public async Task<ReplayAttempt> ExecuteFixedOnceAsync(
        Guid artifactId,
        Func<CancellationToken, Task<ReplayAttempt>> execution,
        CancellationToken cancellationToken)
    {
        var owner = Guid.NewGuid().ToString("N");
        var waitUntil = DateTime.UtcNow.AddSeconds(35);
        while (true)
        {
            var existing = (await GetAttemptsAsync(artifactId, cancellationToken))
                .SingleOrDefault(item => item.TargetMode == ReplayTargetMode.Fixed);
            if (existing is not null) return existing;

            var now = DateTime.UtcNow;
            var expiredBefore = now.AddMinutes(-1);
            var claim = new ReplayExecutionClaimRecord { ArtifactId = artifactId, Owner = owner, ClaimedAtUtc = now };
            context.ReplayExecutionClaims.Add(claim);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException)
            {
                context.Entry(claim).State = EntityState.Detached;
            }

            var claimed = await context.ReplayExecutionClaims
                .Where(item => item.ArtifactId == artifactId && item.ClaimedAtUtc < expiredBefore)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(item => item.Owner, owner)
                    .SetProperty(item => item.ClaimedAtUtc, now), cancellationToken);
            if (claimed == 1)
            {
                existing = (await GetAttemptsAsync(artifactId, cancellationToken))
                    .SingleOrDefault(item => item.TargetMode == ReplayTargetMode.Fixed);
                if (existing is not null) return existing;
                break;
            }
            if (now >= waitUntil) throw new TimeoutException("Another fixed replay is still in progress.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        try
        {
            var attempt = await execution(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await AddAttemptAsync(attempt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return attempt;
        }
        catch
        {
            await context.ReplayExecutionClaims
                .Where(item => item.ArtifactId == artifactId && item.Owner == owner)
                .ExecuteDeleteAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<AgentIterationRecord>> GetIterationsByRunAsync(Guid runId, CancellationToken cancellationToken) =>
        await context.AgentIterations.AsNoTracking().Where(item => item.RunId == runId)
            .OrderBy(item => item.Iteration)
            .Select(item => new AgentIterationRecord(
                item.Id,
                item.RunId,
                item.Iteration,
                item.EvidenceSummary,
                item.Action,
                item.RationaleSummary,
                item.ModelId,
                item.SchemaVersion,
                item.ModelInvocationId,
                item.OccurredAtUtc))
            .ToArrayAsync(cancellationToken);

    private static string[] DeserializeReferences(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
}

internal sealed class FindingProbeCheckpointStore(RaceHunterDbContext context) : IFindingProbeCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FindingProbeCheckpoint?> GetAsync(Guid runId, string probeKey, CancellationToken cancellationToken)
    {
        var item = await context.FindingProbeCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(value => value.RunId == runId && value.ProbeKey == probeKey, cancellationToken);
        return item is null ? null : new FindingProbeCheckpoint(
            item.RunId, item.ProbeKey, item.Phase, item.Ordinal, item.CandidateJson, item.Outcome,
            JsonSerializer.Deserialize<string[]>(item.TraceReferencesJson, JsonOptions) ?? [],
            item.RequestsConsumed, item.CompletedAtUtc);
    }

    public async Task SaveAsync(FindingProbeCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        context.FindingProbeCheckpoints.Add(new FindingProbeCheckpointRecord
        {
            RunId = checkpoint.RunId,
            ProbeKey = checkpoint.ProbeKey,
            Phase = checkpoint.Phase,
            Ordinal = checkpoint.Ordinal,
            CandidateJson = checkpoint.CandidateJson,
            Outcome = checkpoint.Outcome,
            TraceReferencesJson = JsonSerializer.Serialize(checkpoint.TraceReferences, JsonOptions),
            RequestsConsumed = checkpoint.RequestsConsumed,
            CompletedAtUtc = checkpoint.CompletedAtUtc
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            var existing = await GetAsync(checkpoint.RunId, checkpoint.ProbeKey, cancellationToken);
            if (existing is null || existing.Outcome != checkpoint.Outcome ||
                !JsonNode.DeepEquals(JsonNode.Parse(existing.CandidateJson), JsonNode.Parse(checkpoint.CandidateJson)))
                throw;
        }
    }
}
