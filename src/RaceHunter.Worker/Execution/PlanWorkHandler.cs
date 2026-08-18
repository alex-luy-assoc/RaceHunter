using System.Text.Json;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Domain.Budgets;
using RaceHunter.Infrastructure.Observability;

namespace RaceHunter.Worker.Execution;

internal sealed class PlanWorkHandler(
    IHuntStore hunts,
    IManualTargetStore manualTargets,
    IScenarioPlanner planner,
    IWorkInbox inbox) : IPlanWorkHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(
        Guid huntId,
        Guid workId,
        string leaseOwner,
        WorkCheckpoint? checkpoint,
        CancellationToken cancellationToken)
    {
        var hunt = await hunts.GetAsync(huntId, cancellationToken)
            ?? throw new InvalidOperationException("The requested hunt does not exist.");
        if (hunt.Status == HuntStatus.AwaitingApproval) return;
        if (hunt.Status != HuntStatus.Planning) throw new InvalidOperationException("The hunt is not awaiting planning work.");

        var recovered = Recover(checkpoint, hunt.Budget.MaxModelCalls);
        if (recovered.CompletedPlan is not null)
        {
            await hunts.SavePlanAsync(hunt.Id, recovered.CompletedPlan, DateTime.UtcNow, cancellationToken);
            return;
        }
        if (recovered.TerminalOutcome.HasValue)
        {
            await hunts.MarkPlanningFailedAsync(
                hunt.Id,
                recovered.TerminalOutcome.Value,
                recovered.Diagnostic ?? "structured plan validation failed",
                DateTime.UtcNow,
                cancellationToken);
            return;
        }

        var remainingModelCalls = hunt.Budget.MaxModelCalls - recovered.ModelCallsConsumed;
        var remainingBudget = new ExperimentBudget(
            hunt.Budget.MaxActors,
            hunt.Budget.MaxConcurrentActors,
            hunt.Budget.MaxRequests,
            remainingModelCalls,
            hunt.Budget.MaxDuration,
            hunt.Budget.MaxRetries);
        ScenarioPlan plan;
        using var modelActivity = RaceHunterTelemetry.Activities.StartActivity("racehunter.model.plan");
        modelActivity?.SetTag("racehunter.hunt.id", hunt.Id.ToString());
        modelActivity?.SetTag("racehunter.model.id", "gemini-3.5-flash");
        try
        {
            var targetContract = await GetTargetContractAsync(hunt, cancellationToken);
            plan = await planner.PlanAsync(new PlanningContext(
                hunt.Id,
                hunt.Objective,
                targetContract.Operations,
                targetContract.InvariantTypes,
                ["simultaneous-start", "seeded-jitter", "checkpoint-interleaving"],
                remainingBudget,
                targetContract.ObservationMetrics), cancellationToken);
            modelActivity?.SetTag("racehunter.model.invocation_id", plan.ModelInvocationId);
            modelActivity?.SetTag("racehunter.model.schema", plan.SchemaVersion);
        }
        catch (ModelOutputException exception)
        {
            var cumulative = AddUsage(recovered.ModelCallsConsumed, exception.ModelCallsConsumed, hunt.Budget.MaxModelCalls);
            var diagnostic = exception.Outcome == ModelOutcome.BudgetExhausted
                ? "The cumulative planning model-call budget was exhausted. Create a new hunt with a reviewed budget."
                : "structured plan validation failed";
            await SaveUsageAsync(
                workId,
                leaseOwner,
                new PlanningCheckpointState(
                    cumulative,
                    exception.Outcome == ModelOutcome.TransientFailure ? null : exception.Outcome,
                    diagnostic,
                    null),
                cancellationToken);
            if (exception.Outcome == ModelOutcome.TransientFailure) throw;
            await hunts.MarkPlanningFailedAsync(hunt.Id, exception.Outcome, diagnostic, DateTime.UtcNow, CancellationToken.None);
            return;
        }

        var totalUsage = AddUsage(recovered.ModelCallsConsumed, plan.ModelCallsConsumed, hunt.Budget.MaxModelCalls);
        plan = plan with { ModelCallsConsumed = totalUsage };
        await SaveUsageAsync(workId, leaseOwner, new PlanningCheckpointState(totalUsage, null, null, plan), cancellationToken);
        await hunts.SavePlanAsync(hunt.Id, plan, DateTime.UtcNow, cancellationToken);
    }

    private async Task<TargetPlanningContract> GetTargetContractAsync(
        HuntSnapshot hunt,
        CancellationToken cancellationToken)
    {
        if (!hunt.ManualTargetId.HasValue)
            return new TargetPlanningContract(
                [new AllowedTargetOperation("place-order", "POST", "/api/orders")],
                ["numeric-boundary", "cardinality", "cross-observation"],
                ["successful-orders", "inventory-capacity", "order-correlation"]);
        var target = await manualTargets.GetAsync(hunt.ManualTargetId.Value, cancellationToken)
            ?? throw new InvalidOperationException("The hunt's authorized manual target no longer exists.");
        var executable = target.Operations.Where(operation => !operation.IsSetup).ToArray();
        var metrics = executable.SelectMany(operation => operation.ObservationPaths.Keys)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] invariantTypes = ["numeric-boundary"];
        return new TargetPlanningContract(
            executable.Select(operation => new AllowedTargetOperation(operation.Id, operation.Method, operation.Path)).ToArray(),
            invariantTypes,
            metrics);
    }

    private sealed record TargetPlanningContract(
        IReadOnlyList<AllowedTargetOperation> Operations,
        IReadOnlyList<string> InvariantTypes,
        IReadOnlyList<string> ObservationMetrics);

    private async Task SaveUsageAsync(
        Guid workId,
        string leaseOwner,
        PlanningCheckpointState state,
        CancellationToken cancellationToken) =>
        await inbox.SaveCheckpointAsync(
            workId,
            leaseOwner,
            new WorkCheckpoint("planning-model-usage", 0, JsonSerializer.Serialize(state, JsonOptions), DateTime.UtcNow),
            cancellationToken);

    private static PlanningCheckpointState Recover(WorkCheckpoint? checkpoint, int maximumModelCalls)
    {
        if (checkpoint is null) return new PlanningCheckpointState(0, null, null, null);
        if (checkpoint.Boundary != "planning-model-usage")
            throw new InvalidOperationException("The persisted planning checkpoint boundary is unsupported.");
        var state = JsonSerializer.Deserialize<PlanningCheckpointState>(checkpoint.StateJson, JsonOptions)
            ?? throw new InvalidOperationException("The persisted planning checkpoint is invalid.");
        if (state.ModelCallsConsumed < 0 || state.ModelCallsConsumed > maximumModelCalls)
            throw new InvalidOperationException("The persisted planning model usage exceeds the server budget.");
        return state;
    }

    private static int AddUsage(int previous, int current, int maximum)
    {
        var cumulative = checked(previous + current);
        if (cumulative > maximum)
            throw new InvalidOperationException("The planning adapter reported model usage beyond the server budget.");
        return cumulative;
    }

    private sealed record PlanningCheckpointState(
        int ModelCallsConsumed,
        ModelOutcome? TerminalOutcome,
        string? Diagnostic,
        ScenarioPlan? CompletedPlan);
}
