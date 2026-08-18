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
        var inbox = await context.WorkInbox.SingleOrDefaultAsync(item =>
            item.WorkId == workId && item.Status == "Processing" && item.LeaseOwner == leaseOwner, cancellationToken)
            ?? throw new InvalidOperationException("The decision checkpoint lease is no longer owned by this worker.");
        if (await context.AgentIterations.AnyAsync(item => item.RunId == iteration.RunId && item.Iteration == iteration.Iteration, cancellationToken))
            throw new InvalidOperationException("The agent iteration was already persisted without its checkpoint.");

        var cursor = (await context.RunEvents
            .Where(item => item.RunId == iteration.RunId)
            .Select(item => (long?)item.Cursor)
            .MaxAsync(cancellationToken) ?? 0) + 1;
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
        inbox.CheckpointBoundary = checkpoint.Boundary;
        inbox.CheckpointIteration = checkpoint.Iteration;
        inbox.CheckpointStateJson = checkpoint.StateJson;
        inbox.CheckpointAtUtc = checkpoint.PersistedAtUtc;
        inbox.UpdatedAtUtc = checkpoint.PersistedAtUtc;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }
}
