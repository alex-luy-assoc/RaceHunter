using RaceHunter.Application.Agents;
using RaceHunter.Application.Messaging;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Common;

namespace RaceHunter.Application.Hunts;

public enum HuntStatus
{
    Draft,
    Planning,
    AwaitingApproval,
    Queued,
    PlanningFailed
}

public sealed record HuntSnapshot(
    Guid Id,
    string Objective,
    ExperimentBudget Budget,
    HuntStatus Status,
    ScenarioPlan? Plan,
    string? ApprovedPlanVersion,
    Guid? RunId,
    DateTime CreatedAtUtc,
    string? FailureOutcome = null,
    string? FailureDiagnostic = null,
    Guid? ManualTargetId = null);

public sealed record HuntEvent(long Cursor, string Kind, string Message, DateTime OccurredAtUtc);

public interface IHuntStore
{
    Task AddAsync(HuntSnapshot hunt, CancellationToken cancellationToken);
    Task<HuntSnapshot?> GetAsync(Guid huntId, CancellationToken cancellationToken);
    Task<HuntSnapshot?> GetByRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HuntEvent>> GetEventsAsync(Guid huntId, long after, CancellationToken cancellationToken);
    Task<WorkDispatch?> RequestPlanningAsync(Guid huntId, DateTime nowUtc, CancellationToken cancellationToken);
    Task SavePlanAsync(Guid huntId, ScenarioPlan plan, DateTime nowUtc, CancellationToken cancellationToken);
    Task MarkPlanningFailedAsync(Guid huntId, ModelOutcome outcome, string sanitizedDiagnostic, DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed class CreateHunt(IHuntStore store, IManualTargetStore manualTargets)
{
    public async Task<HuntSnapshot> ExecuteAsync(string objective, ExperimentBudget budget, CancellationToken cancellationToken)
        => await ExecuteAsync(objective, budget, null, cancellationToken);

    public async Task<HuntSnapshot> ExecuteAsync(
        string objective,
        ExperimentBudget budget,
        Guid? manualTargetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objective)) throw new DomainException("A business objective is required.");
        if (manualTargetId.HasValue && await manualTargets.GetAsync(manualTargetId.Value, cancellationToken) is null)
            throw new DomainException("The authorized manual target does not exist.");
        var hunt = new HuntSnapshot(Guid.NewGuid(), objective.Trim(), budget, HuntStatus.Draft, null, null, null, DateTime.UtcNow, ManualTargetId: manualTargetId);
        await store.AddAsync(hunt, cancellationToken);
        return hunt;
    }
}

public sealed class GeneratePlan(IHuntStore store)
{
    public async Task<WorkDispatch> ExecuteAsync(Guid huntId, CancellationToken cancellationToken) =>
        await store.RequestPlanningAsync(huntId, DateTime.UtcNow, cancellationToken)
        ?? throw new DomainException("Planning was already requested or the hunt does not exist.");
}

public sealed record AgentIterationRecord(
    Guid Id,
    Guid RunId,
    int Iteration,
    string EvidenceSummary,
    string Action,
    string RationaleSummary,
    string ModelId,
    string SchemaVersion,
    string ModelInvocationId,
    DateTime OccurredAtUtc);

public interface IAgentIterationStore
{
    Task AppendAsync(AgentIterationRecord iteration, CancellationToken cancellationToken);
}

public interface IAgentDecisionCheckpointStore
{
    Task PersistAsync(
        Guid workId,
        string leaseOwner,
        AgentIterationRecord iteration,
        Messaging.WorkCheckpoint checkpoint,
        string eventMessage,
        CancellationToken cancellationToken);
}

public sealed record OutboxItem(Guid Id, WorkDispatch Work, int PublishAttempts, DateTime CreatedAtUtc);

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxItem>> GetPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(Guid outboxId, DateTime publishedAtUtc, CancellationToken cancellationToken);
    Task RecordFailureAsync(Guid outboxId, CancellationToken cancellationToken);
}

public sealed class OutboxDispatcher(IOutboxStore store, IWorkPublisher publisher)
{
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var dispatched = 0;
        foreach (var item in await store.GetPendingAsync(50, cancellationToken))
        {
            try
            {
                await publisher.PublishAsync(item.Work, cancellationToken);
                await store.MarkPublishedAsync(item.Id, DateTime.UtcNow, cancellationToken);
                dispatched++;
            }
            catch
            {
                await store.RecordFailureAsync(item.Id, cancellationToken);
                throw;
            }
        }
        return dispatched;
    }
}
