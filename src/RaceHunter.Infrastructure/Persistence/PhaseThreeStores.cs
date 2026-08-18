using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Common;
using RaceHunter.Domain.Runs;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class HuntWorkflowStore(RaceHunterDbContext context) : IHuntStore, IHuntWorkflowStore, IOutboxStore, IAgentIterationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AddAsync(HuntSnapshot hunt, CancellationToken cancellationToken)
    {
        context.Hunts.Add(new HuntRecord
        {
            Id = hunt.Id,
            Objective = hunt.Objective,
            Status = hunt.Status.ToString(),
            MaxActors = hunt.Budget.MaxActors,
            MaxConcurrentActors = hunt.Budget.MaxConcurrentActors,
            MaxRequests = hunt.Budget.MaxRequests,
            MaxModelCalls = hunt.Budget.MaxModelCalls,
            MaxDurationMilliseconds = checked((long)hunt.Budget.MaxDuration.TotalMilliseconds),
            MaxRetries = hunt.Budget.MaxRetries,
            CreatedAtUtc = hunt.CreatedAtUtc
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<HuntSnapshot?> GetAsync(Guid huntId, CancellationToken cancellationToken)
    {
        var record = await context.Hunts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == huntId, cancellationToken);
        return record is null ? null : ToSnapshot(record);
    }

    public async Task<HuntSnapshot?> GetByRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var record = await context.Hunts.AsNoTracking().SingleOrDefaultAsync(item => item.RunId == runId, cancellationToken);
        return record is null ? null : ToSnapshot(record);
    }

    public async Task<IReadOnlyList<HuntEvent>> GetEventsAsync(Guid huntId, long after, CancellationToken cancellationToken) =>
        await context.HuntEvents.AsNoTracking()
            .Where(item => item.HuntId == huntId && item.Cursor > Math.Max(0, after))
            .OrderBy(item => item.Cursor)
            .Take(100)
            .Select(item => new HuntEvent(item.Cursor, item.Kind, item.Message, item.OccurredAtUtc))
            .ToArrayAsync(cancellationToken);

    public async Task<WorkDispatch?> RequestPlanningAsync(Guid huntId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var work = new WorkDispatch("work-v1", Guid.NewGuid(), WorkKind.PlanRequested, huntId, $"hunt:{huntId:N}", EnsureUtc(nowUtc));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var updated = await context.Hunts
            .Where(item => item.Id == huntId && item.Status == nameof(HuntStatus.Draft))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, nameof(HuntStatus.Planning)), cancellationToken);
        if (updated == 0) return null;
        context.HuntEvents.Add(new HuntEventRecord
        {
            HuntId = huntId,
            Cursor = 1,
            Kind = "plan-requested",
            Message = "Schema-constrained planning was queued.",
            OccurredAtUtc = EnsureUtc(nowUtc)
        });
        context.OutboxMessages.Add(ToOutbox(work, nowUtc));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return work;
    }

    public async Task SavePlanAsync(Guid huntId, ScenarioPlan plan, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var updated = await context.Hunts
            .Where(item => item.Id == huntId && item.Status == nameof(HuntStatus.Planning))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, nameof(HuntStatus.AwaitingApproval))
                .SetProperty(item => item.PlanVersion, plan.PlanVersion)
                .SetProperty(item => item.PlanJson, JsonSerializer.Serialize(plan, JsonOptions))
                .SetProperty(item => item.FailureOutcome, (string?)null)
                .SetProperty(item => item.FailureDiagnostic, (string?)null), cancellationToken);
        if (updated == 0) throw new DomainException("Only a planning hunt can receive a plan.");
        context.HuntEvents.Add(new HuntEventRecord
        {
            HuntId = huntId,
            Cursor = 2,
            Kind = "plan-ready",
            Message = $"Validated plan {plan.PlanVersion} is ready for one-time approval.",
            OccurredAtUtc = EnsureUtc(nowUtc)
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkPlanningFailedAsync(Guid huntId, ModelOutcome outcome, string sanitizedDiagnostic, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var updated = await context.Hunts
            .Where(item => item.Id == huntId && item.Status == nameof(HuntStatus.Planning))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, nameof(HuntStatus.PlanningFailed))
                .SetProperty(item => item.FailureOutcome, outcome.ToString())
                .SetProperty(item => item.FailureDiagnostic, sanitizedDiagnostic), cancellationToken);
        if (updated == 0) throw new DomainException("Only a planning hunt can fail planning.");
        context.HuntEvents.Add(new HuntEventRecord
        {
            HuntId = huntId,
            Cursor = 2,
            Kind = "model-failed",
            Message = "Schema-constrained planning failed after one repair attempt.",
            OccurredAtUtc = EnsureUtc(nowUtc)
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ApprovalResult> ApproveAndQueueAsync(
        Guid huntId,
        string requestedPlanVersion,
        string idempotencyKey,
        Guid requestedRunId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await context.Hunts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == huntId, cancellationToken)
            ?? throw new DomainException("The hunt does not exist.");
        if (existing.ApprovalKey is not null)
        {
            if (existing.ApprovalKey == idempotencyKey && existing.ApprovedPlanVersion == requestedPlanVersion && existing.RunId.HasValue)
                return new ApprovalResult(existing.RunId.Value, existing.ApprovedPlanVersion, existing.ApprovalKey);
            throw new DomainException("The plan was already approved.");
        }
        if (existing.Status != nameof(HuntStatus.AwaitingApproval) || existing.PlanVersion != requestedPlanVersion)
            throw new DomainException("The requested plan version is stale or unavailable.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var updated = await context.Hunts
            .Where(item => item.Id == huntId && item.Status == nameof(HuntStatus.AwaitingApproval) &&
                item.PlanVersion == requestedPlanVersion && item.ApprovalKey == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, nameof(HuntStatus.Queued))
                .SetProperty(item => item.ApprovedPlanVersion, requestedPlanVersion)
                .SetProperty(item => item.ApprovalKey, idempotencyKey)
                .SetProperty(item => item.RunId, requestedRunId), cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var concurrent = await context.Hunts.AsNoTracking().SingleAsync(item => item.Id == huntId, cancellationToken);
            if (concurrent.ApprovalKey == idempotencyKey && concurrent.ApprovedPlanVersion == requestedPlanVersion && concurrent.RunId.HasValue)
                return new ApprovalResult(concurrent.RunId.Value, concurrent.ApprovedPlanVersion, concurrent.ApprovalKey);
            throw new DomainException("The plan was approved concurrently.");
        }

        context.Runs.Add(new RunRecord
        {
            Id = requestedRunId,
            Status = nameof(RunStatus.Queued),
            MaxActors = existing.MaxActors,
            MaxConcurrentActors = existing.MaxConcurrentActors,
            MaxRequests = existing.MaxRequests,
            MaxModelCalls = existing.MaxModelCalls,
            MaxDurationMilliseconds = existing.MaxDurationMilliseconds,
            MaxRetries = existing.MaxRetries,
            CreatedAtUtc = EnsureUtc(nowUtc)
        });
        var work = new WorkDispatch("work-v1", Guid.NewGuid(), WorkKind.RunRequested, requestedRunId, $"hunt:{huntId:N}", EnsureUtc(nowUtc));
        context.OutboxMessages.Add(ToOutbox(work, nowUtc));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ApprovalResult(requestedRunId, requestedPlanVersion, idempotencyKey);
    }

    public async Task<IReadOnlyList<OutboxItem>> GetPendingAsync(int limit, CancellationToken cancellationToken) =>
        await context.OutboxMessages.AsNoTracking()
            .Where(item => item.PublishedAtUtc == null)
            .OrderBy(item => item.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(item => new OutboxItem(item.Id, new WorkDispatch(
                item.Version,
                item.WorkId,
                Enum.Parse<WorkKind>(item.Kind),
                item.SubjectId,
                item.CorrelationId,
                item.WorkCreatedAtUtc), item.PublishAttempts, item.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

    public Task MarkPublishedAsync(Guid outboxId, DateTime publishedAtUtc, CancellationToken cancellationToken) =>
        context.OutboxMessages.Where(item => item.Id == outboxId && item.PublishedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PublishedAtUtc, EnsureUtc(publishedAtUtc)), cancellationToken);

    public Task RecordFailureAsync(Guid outboxId, CancellationToken cancellationToken) =>
        context.OutboxMessages.Where(item => item.Id == outboxId && item.PublishedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PublishAttempts, item => item.PublishAttempts + 1), cancellationToken);

    public async Task AppendAsync(AgentIterationRecord iteration, CancellationToken cancellationToken)
    {
        context.AgentIterations.Add(new AgentIterationPersistenceRecord
        {
            Id = iteration.Id,
            RunId = iteration.RunId,
            Iteration = iteration.Iteration,
            EvidenceSummary = iteration.EvidenceSummary,
            Action = iteration.Action,
            RationaleSummary = iteration.RationaleSummary,
            ModelId = iteration.ModelId,
            SchemaVersion = iteration.SchemaVersion,
            ModelInvocationId = iteration.ModelInvocationId,
            OccurredAtUtc = iteration.OccurredAtUtc
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static HuntSnapshot ToSnapshot(HuntRecord item) => new(
        item.Id,
        item.Objective,
        new ExperimentBudget(item.MaxActors, item.MaxConcurrentActors, item.MaxRequests, item.MaxModelCalls, TimeSpan.FromMilliseconds(item.MaxDurationMilliseconds), item.MaxRetries),
        Enum.Parse<HuntStatus>(item.Status),
        item.PlanJson is null ? null : JsonSerializer.Deserialize<ScenarioPlan>(item.PlanJson, JsonOptions),
        item.ApprovedPlanVersion,
        item.RunId,
        item.CreatedAtUtc);

    private static OutboxRecord ToOutbox(WorkDispatch work, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        Version = work.Version,
        WorkId = work.WorkId,
        Kind = work.Kind.ToString(),
        SubjectId = work.SubjectId,
        CorrelationId = work.CorrelationId,
        WorkCreatedAtUtc = work.CreatedAtUtc,
        CreatedAtUtc = EnsureUtc(nowUtc)
    };

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

internal sealed class WorkInboxStore(RaceHunterDbContext context) : IWorkInbox
{
    public async Task<WorkAcquireResult> TryAcquireAsync(Guid workId, string messageId, string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        Validate(workId, messageId, owner, leaseDuration);
        nowUtc = EnsureUtc(nowUtc);
        var record = await context.WorkInbox.SingleOrDefaultAsync(item => item.WorkId == workId, cancellationToken);
        if (record is null)
        {
            record = new WorkInboxRecord
            {
                WorkId = workId,
                MessageId = messageId,
                Status = "Processing",
                DeliveryAttempt = 1,
                LeaseOwner = owner,
                LeaseExpiresAtUtc = nowUtc + leaseDuration,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            context.WorkInbox.Add(record);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return new WorkAcquireResult(WorkAcquireOutcome.Acquired, 1, null);
            }
            catch (DbUpdateException)
            {
                context.Entry(record).State = EntityState.Detached;
                record = await context.WorkInbox.SingleAsync(item => item.WorkId == workId, cancellationToken);
            }
        }

        if (record.Status is "Completed" or "DeadLettered")
            return new WorkAcquireResult(WorkAcquireOutcome.Duplicate, record.DeliveryAttempt, ToCheckpoint(record));
        if (record.Status == "RetryScheduled" && record.LeaseExpiresAtUtc > nowUtc)
            return new WorkAcquireResult(WorkAcquireOutcome.RetryLater, record.DeliveryAttempt, ToCheckpoint(record));
        if (record.Status == "Processing" && record.LeaseExpiresAtUtc > nowUtc)
            return new WorkAcquireResult(WorkAcquireOutcome.Busy, record.DeliveryAttempt, ToCheckpoint(record));

        record.Status = "Processing";
        record.MessageId = messageId;
        record.DeliveryAttempt++;
        record.LeaseOwner = owner;
        record.LeaseExpiresAtUtc = nowUtc + leaseDuration;
        record.UpdatedAtUtc = nowUtc;
        await context.SaveChangesAsync(cancellationToken);
        return new WorkAcquireResult(WorkAcquireOutcome.Resumed, record.DeliveryAttempt, ToCheckpoint(record));
    }

    public async Task<bool> HeartbeatAsync(Guid workId, string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var updated = await context.WorkInbox
            .Where(item => item.WorkId == workId && item.Status == "Processing" && item.LeaseOwner == owner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseExpiresAtUtc, EnsureUtc(nowUtc) + leaseDuration)
                .SetProperty(item => item.UpdatedAtUtc, EnsureUtc(nowUtc)), cancellationToken);
        context.ChangeTracker.Clear();
        return updated == 1;
    }

    public async Task SaveCheckpointAsync(Guid workId, string owner, WorkCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var updated = await context.WorkInbox
            .Where(item => item.WorkId == workId && item.Status == "Processing" && item.LeaseOwner == owner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CheckpointBoundary, checkpoint.Boundary)
                .SetProperty(item => item.CheckpointIteration, checkpoint.Iteration)
                .SetProperty(item => item.CheckpointStateJson, checkpoint.StateJson)
                .SetProperty(item => item.CheckpointAtUtc, EnsureUtc(checkpoint.PersistedAtUtc))
                .SetProperty(item => item.UpdatedAtUtc, EnsureUtc(checkpoint.PersistedAtUtc)), cancellationToken);
        if (updated != 1) throw new InvalidOperationException("The checkpoint lease is no longer owned by this worker.");
        context.ChangeTracker.Clear();
    }

    public async Task CompleteAsync(Guid workId, string owner, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var updated = await context.WorkInbox
            .Where(item => item.WorkId == workId && item.Status == "Processing" && item.LeaseOwner == owner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "Completed")
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(item => item.UpdatedAtUtc, EnsureUtc(nowUtc)), cancellationToken);
        if (updated != 1) throw new InvalidOperationException("The completion lease is no longer owned by this worker.");
        context.ChangeTracker.Clear();
    }

    public async Task<WorkFailureOutcome> RecordFailureAsync(Guid workId, string owner, WorkFailure failure, int maxRetries, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var record = await context.WorkInbox.SingleOrDefaultAsync(item => item.WorkId == workId, cancellationToken)
            ?? throw new InvalidOperationException("The work inbox item does not exist.");
        if (record.Status != "Processing" || record.LeaseOwner != owner) throw new InvalidOperationException("The failure lease is no longer owned by this worker.");
        record.FailureCategory = failure.Category.ToString();
        record.FailureDiagnostic = failure.SanitizedDiagnostic;
        record.UpdatedAtUtc = EnsureUtc(nowUtc);
        record.LeaseOwner = null;
        record.LeaseExpiresAtUtc = null;
        if (failure.Transient && failure.OperationIsIdempotent && record.DeliveryAttempt <= maxRetries)
        {
            record.Status = "RetryScheduled";
            record.LeaseExpiresAtUtc = EnsureUtc(nowUtc) + WorkRetryPolicy.Delay(record.DeliveryAttempt, workId);
            await context.SaveChangesAsync(cancellationToken);
            return WorkFailureOutcome.RetryScheduled;
        }

        record.Status = "DeadLettered";
        context.DeadLetters.Add(new DeadLetterRecord
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            Category = failure.Category.ToString(),
            Diagnostic = failure.SanitizedDiagnostic,
            CreatedAtUtc = EnsureUtc(nowUtc)
        });
        await context.SaveChangesAsync(cancellationToken);
        return WorkFailureOutcome.DeadLettered;
    }

    private static WorkCheckpoint? ToCheckpoint(WorkInboxRecord item) =>
        item.CheckpointBoundary is null || item.CheckpointIteration is null || item.CheckpointStateJson is null || item.CheckpointAtUtc is null
            ? null
            : new WorkCheckpoint(item.CheckpointBoundary, item.CheckpointIteration.Value, item.CheckpointStateJson, item.CheckpointAtUtc.Value);

    private static void Validate(Guid workId, string messageId, string owner, TimeSpan leaseDuration)
    {
        if (workId == Guid.Empty) throw new ArgumentException("A work ID is required.", nameof(workId));
        if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentException("A message ID is required.", nameof(messageId));
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("A lease owner is required.", nameof(owner));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
