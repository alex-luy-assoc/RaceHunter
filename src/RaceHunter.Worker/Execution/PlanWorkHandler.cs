using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;

namespace RaceHunter.Worker.Execution;

internal sealed class PlanWorkHandler(IHuntStore hunts, IScenarioPlanner planner) : IPlanWorkHandler
{
    public async Task ExecuteAsync(Guid huntId, CancellationToken cancellationToken)
    {
        var hunt = await hunts.GetAsync(huntId, cancellationToken)
            ?? throw new InvalidOperationException("The requested hunt does not exist.");
        if (hunt.Status == HuntStatus.AwaitingApproval) return;
        if (hunt.Status != HuntStatus.Planning) throw new InvalidOperationException("The hunt is not awaiting planning work.");
        try
        {
            var plan = await planner.PlanAsync(new PlanningContext(
                hunt.Id,
                hunt.Objective,
                [new AllowedTargetOperation("place-order", "POST", "/api/orders")],
                ["numeric-boundary", "cardinality", "cross-observation"],
                ["simultaneous-start", "seeded-jitter", "checkpoint-interleaving"],
                hunt.Budget,
                ["successful-orders", "inventory-capacity", "order-correlation"]), cancellationToken);
            await hunts.SavePlanAsync(hunt.Id, plan, DateTime.UtcNow, cancellationToken);
        }
        catch (ModelOutputException exception) when (exception.Outcome == ModelOutcome.TransientFailure)
        {
            throw;
        }
        catch (ModelOutputException exception)
        {
            await hunts.MarkPlanningFailedAsync(hunt.Id, exception.Outcome, "structured plan validation failed", DateTime.UtcNow, CancellationToken.None);
        }
    }
}
