using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using RaceHunter.Infrastructure.Observability;

namespace RaceHunter.Worker.Execution;

internal sealed class ReferenceInventoryTargetClient(HttpClient client)
{
    internal static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(30);

    public async Task ResetAsync(ReplayTargetMode mode, string demoControlKey, CancellationToken cancellationToken)
        => await ResetAsync(mode, demoControlKey, null, cancellationToken);

    public async Task ResetAsync(ReplayTargetMode mode, string demoControlKey, string? operationKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(demoControlKey)) throw new InvalidOperationException("ReferenceTarget:DemoControlKey is required for replay.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/demo/reset")
        {
            Content = JsonContent.Create(new { quantity = 1, mode = mode == ReplayTargetMode.Fixed ? "fixed" : "vulnerable" })
        };
        request.Headers.Add("X-Demo-Control-Key", demoControlKey);
        if (operationKey is not null)
        {
            request.Headers.Add("X-RaceHunter-Idempotency-Key", TargetKey(operationKey));
            var replayScope = operationKey.EndsWith(":reset", StringComparison.Ordinal)
                ? operationKey[..^":reset".Length]
                : operationKey;
            request.Headers.Add("X-RaceHunter-Replay-Scope", TargetKey(replayScope));
        }
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TargetCallResult> PlaceOrderAsync(Guid runId, ScheduledActor actor, CancellationToken cancellationToken)
        => await PlaceOrderAsync(runId, actor, null, cancellationToken);

    public async Task<TargetCallResult> PlaceOrderAsync(Guid runId, ScheduledActor actor, string? operationKey, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var activity = RaceHunterTelemetry.Activities.StartActivity("racehunter.target.place-order", System.Diagnostics.ActivityKind.Client);
        activity?.SetTag("racehunter.run.id", runId.ToString());
        activity?.SetTag("racehunter.actor.id", actor.ActorId);
        activity?.SetTag("racehunter.step.id", actor.OperationKey);
        var requestId = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/orders", new
        {
            actorId = $"actor-{actor.ActorId}",
            quantity = 1,
            checkpoint = actor.CheckpointOrder.HasValue ? $"oversell:{runId:N}" : string.Empty,
            idempotencyKey = operationKey is null ? null : OrderKey(operationKey, actor.ActorId, actor.OperationKey),
            replayScope = operationKey is null ? null : TargetKey(operationKey)
        }, cancellationToken);

        RaceHunterTelemetry.TargetLatency.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var rejected = await response.Content.ReadFromJsonAsync<InventoryOrderResponse>(cancellationToken);
            return TargetCallResult.Failure(requestId: rejected?.CorrelationId.ToString("N") ?? requestId, reused: rejected?.Replayed ?? false);
        }
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<InventoryOrderResponse>(cancellationToken);
        if (body is null) throw new InvalidOperationException("The reference target returned no order evidence.");
        var targetCorrelationId = body.CorrelationId.ToString("N");
        activity?.SetTag("racehunter.request.id", targetCorrelationId);
        return TargetCallResult.Success(
            [
                Observation.Number("successful-orders", body.SuccessfulOrders, $"target-response:{targetCorrelationId}", targetCorrelationId),
                Observation.Number("inventory-capacity", 1, $"target-response:{targetCorrelationId}", targetCorrelationId),
                Observation.Text("order-correlation", targetCorrelationId, $"target-response:{targetCorrelationId}", targetCorrelationId)
            ],
            targetCorrelationId,
            body.Replayed);
    }

    public async Task<int> CountMissingOrdersAsync(
        ReplayCandidate candidate,
        string operationKey,
        string demoControlKey,
        IReadOnlySet<string> persistedRequestIds,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/demo/order-status")
        {
            Content = JsonContent.Create(new
            {
                idempotencyKeys = candidate.Steps.Select((step, index) => OrderKey(
                    operationKey,
                    step.ActorId,
                    $"{index}:{step.StepId}:{step.OperationId}")).ToArray()
            })
        };
        request.Headers.Add("X-Demo-Control-Key", demoControlKey);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<OrderStatusResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The reference target returned no durable operation status.");
        return status.Missing + status.Completed.Count(item => !persistedRequestIds.Contains(item.RequestId));
    }

    private static string TargetKey(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string OrderKey(string operationKey, int actorId, string stepKey) => TargetKey($"{operationKey}:actor:{actorId}:step:{stepKey}");

    private sealed record InventoryOrderResponse(Guid CorrelationId, int SuccessfulOrders, bool Replayed);
    private sealed record CompletedOrderStatus(string IdempotencyKey, string RequestId);
    private sealed record OrderStatusResponse(int Missing, IReadOnlyList<CompletedOrderStatus> Completed);
}
