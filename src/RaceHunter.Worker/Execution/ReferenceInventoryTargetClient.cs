using System.Net;
using System.Net.Http.Json;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Invariants;

namespace RaceHunter.Worker.Execution;

internal sealed class ReferenceInventoryTargetClient(HttpClient client)
{
    public async Task<TargetCallResult> PlaceOrderAsync(Guid runId, ScheduledActor actor, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/orders", new
        {
            actorId = $"actor-{actor.ActorId}",
            quantity = 1,
            checkpoint = actor.CheckpointOrder.HasValue ? $"oversell:{runId:N}" : string.Empty
        }, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
            return TargetCallResult.Failure(requestId: requestId);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<InventoryOrderResponse>(cancellationToken);
        if (body is null) throw new InvalidOperationException("The reference target returned no order evidence.");
        var targetCorrelationId = body.CorrelationId.ToString("N");
        return TargetCallResult.Success(
            [
                Observation.Number("successful-orders", body.SuccessfulOrders, $"target-response:{targetCorrelationId}", targetCorrelationId),
                Observation.Number("inventory-capacity", 1, $"target-response:{targetCorrelationId}", targetCorrelationId),
                Observation.Text("order-correlation", targetCorrelationId, $"target-response:{targetCorrelationId}", targetCorrelationId)
            ],
            targetCorrelationId);
    }

    private sealed record InventoryOrderResponse(Guid CorrelationId, int SuccessfulOrders);
}
