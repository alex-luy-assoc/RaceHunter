using System.Data;
using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Hunts;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class ManualSetupExecutionStore(RaceHunterDbContext context) : IManualSetupExecutionStore
{
    public async Task<ManualSetupClaim> ReserveAsync(Guid runId, Guid targetId, string executionKey,
        string operationId, string idempotencyMode, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await context.Runs.FromSqlInterpolated($"SELECT * FROM experiment_runs WHERE id = {runId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var existing = await context.ManualSetupExecutions.SingleOrDefaultAsync(item =>
            item.RunId == runId && item.ExecutionKey == executionKey && item.OperationId == operationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == "completed")
                return await CommitAsync(new ManualSetupClaim(ManualSetupClaimDisposition.Completed, existing.PhysicalRequestsReserved));
            if (existing.Status == "ambiguous" || existing.IdempotencyMode != ManualTargetIdempotencyModes.ReceiverKeyed)
            {
                existing.Status = "ambiguous";
                await context.SaveChangesAsync(cancellationToken);
                return await CommitAsync(new ManualSetupClaim(ManualSetupClaimDisposition.Ambiguous, existing.PhysicalRequestsReserved));
            }
        }

        var used = await PhysicalRequestsAsync(runId, cancellationToken);
        if (used >= run.MaxRequests)
            return await CommitAsync(new ManualSetupClaim(ManualSetupClaimDisposition.BudgetExceeded,
                existing?.PhysicalRequestsReserved ?? 0));
        if (existing is null)
        {
            existing = new ManualSetupExecutionRecord
            {
                RunId = runId,
                TargetId = targetId,
                ExecutionKey = executionKey,
                OperationId = operationId,
                IdempotencyMode = idempotencyMode,
                Status = "reserved",
                PhysicalRequestsReserved = 1,
                ReservedAtUtc = DateTime.UtcNow
            };
            context.ManualSetupExecutions.Add(existing);
        }
        else
        {
            existing.PhysicalRequestsReserved++;
            existing.ReservedAtUtc = DateTime.UtcNow;
        }
        await context.SaveChangesAsync(cancellationToken);
        return await CommitAsync(new ManualSetupClaim(ManualSetupClaimDisposition.Send, existing.PhysicalRequestsReserved));

        async Task<ManualSetupClaim> CommitAsync(ManualSetupClaim result)
        {
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }

    public async Task CompleteAsync(Guid runId, string executionKey, string operationId, CancellationToken cancellationToken)
    {
        var item = await FindAsync(runId, executionKey, operationId, cancellationToken);
        item.Status = "completed";
        item.CompletedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAmbiguousAsync(Guid runId, string executionKey, string operationId, CancellationToken cancellationToken)
    {
        var item = await FindAsync(runId, executionKey, operationId, cancellationToken);
        item.Status = "ambiguous";
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> CanStartAsync(Guid runId, int additionalRequests, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await context.Runs.FromSqlInterpolated($"SELECT * FROM experiment_runs WHERE id = {runId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var allowed = additionalRequests <= run.MaxRequests - await PhysicalRequestsAsync(runId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return allowed;
    }

    private async Task<int> PhysicalRequestsAsync(Guid runId, CancellationToken cancellationToken) => checked(
        await context.TraceEvents.CountAsync(item => item.RunId == runId, cancellationToken) +
        await context.ManualSetupExecutions.Where(item => item.RunId == runId)
            .SumAsync(item => (int?)item.PhysicalRequestsReserved, cancellationToken) ?? 0);

    private async Task<ManualSetupExecutionRecord> FindAsync(Guid runId, string executionKey, string operationId,
        CancellationToken cancellationToken) =>
        await context.ManualSetupExecutions.SingleAsync(item => item.RunId == runId &&
            item.ExecutionKey == executionKey && item.OperationId == operationId, cancellationToken);
}
