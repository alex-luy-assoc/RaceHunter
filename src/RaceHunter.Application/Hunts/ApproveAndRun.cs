using RaceHunter.Domain.Common;

namespace RaceHunter.Application.Hunts;

public sealed record ApprovalResult(Guid RunId, string PlanVersion, string IdempotencyKey);

public interface IHuntWorkflowStore
{
    Task<ApprovalResult> ApproveAndQueueAsync(
        Guid huntId,
        string requestedPlanVersion,
        string idempotencyKey,
        Guid requestedRunId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

public sealed class ApproveAndRun(IHuntWorkflowStore store)
{
    public Task<ApprovalResult> ExecuteAsync(
        Guid huntId,
        string planVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (huntId == Guid.Empty) throw new DomainException("A hunt ID is required.");
        if (string.IsNullOrWhiteSpace(planVersion)) throw new DomainException("A plan version is required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new DomainException("An approval idempotency key is required.");
        return store.ApproveAndQueueAsync(
            huntId,
            planVersion.Trim(),
            idempotencyKey.Trim(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            cancellationToken);
    }
}
