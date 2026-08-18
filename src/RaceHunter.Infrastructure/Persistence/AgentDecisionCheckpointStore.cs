using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class AgentDecisionCheckpointStore(RaceHunterDbContext context) : IAgentDecisionCheckpointStore
{
    public async Task PersistAsync(
        Guid workId,
        string leaseOwner,
        AgentIterationRecord iteration,
        WorkCheckpoint checkpoint,
        string eventMessage,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.AgentIterations.AnyAsync(item => item.RunId == iteration.RunId && item.Iteration == iteration.Iteration, cancellationToken))
            throw new InvalidOperationException("The agent iteration was already persisted without its checkpoint.");

        var cursor = (await context.RunEvents
            .Where(item => item.RunId == iteration.RunId)
            .Select(item => (long?)item.Cursor)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var leaseUpdated = await context.WorkInbox
            .Where(item => item.WorkId == workId && item.Status == "Processing" && item.LeaseOwner == leaseOwner && item.LeaseExpiresAtUtc > checkpoint.PersistedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CheckpointBoundary, checkpoint.Boundary)
                .SetProperty(item => item.CheckpointIteration, checkpoint.Iteration)
                .SetProperty(item => item.CheckpointStateJson, checkpoint.StateJson)
                .SetProperty(item => item.CheckpointAtUtc, checkpoint.PersistedAtUtc)
                .SetProperty(item => item.UpdatedAtUtc, checkpoint.PersistedAtUtc), cancellationToken);
        if (leaseUpdated != 1)
            throw new WorkLeaseLostException("The decision checkpoint lease is no longer owned by this worker.");

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
        context.RunEvents.Add(new RunEventRecord
        {
            RunId = iteration.RunId,
            Cursor = cursor,
            Kind = "agent-decision",
            Message = eventMessage,
            OccurredAtUtc = iteration.OccurredAtUtc
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }
}
