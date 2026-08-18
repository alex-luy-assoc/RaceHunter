using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Invariants;
using RaceHunter.Worker.Execution;
using Xunit;

namespace RaceHunter.Worker.Tests;

public sealed class ReferenceInventoryTargetClientTests
{
    [Fact]
    public async Task Reference_operation_emits_executable_cross_observation_and_cardinality_evidence()
    {
        using var http = new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://reference-target") };
        var result = await new ReferenceInventoryTargetClient(http).PlaceOrderAsync(
            Guid.NewGuid(),
            new ScheduledActor(1, TimeSpan.Zero),
            CancellationToken.None);

        var cross = new InvariantEvaluatorRegistry().Evaluate(
            new CrossObservationInvariant("successful-orders", "inventory-capacity", CrossObservationRelation.LessThanOrEqual),
            result.Observations);
        var cardinality = new InvariantEvaluatorRegistry().Evaluate(new CardinalityInvariant("order-correlation"), result.Observations);
        Assert.Equal(InvariantOutcome.Pass, cross.Outcome);
        Assert.Equal(InvariantOutcome.Pass, cardinality.Outcome);
    }

    [Fact]
    public async Task Durable_target_keys_distinguish_two_steps_for_the_same_actor()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://reference-target") };
        var client = new ReferenceInventoryTargetClient(http);

        await client.PlaceOrderAsync(Guid.NewGuid(), new ScheduledActor(1, TimeSpan.Zero, null, "0:read:read"), "probe", CancellationToken.None);
        await client.PlaceOrderAsync(Guid.NewGuid(), new ScheduledActor(1, TimeSpan.Zero, null, "1:write:write"), "probe", CancellationToken.None);

        Assert.Equal(2, handler.Keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Verify_fix_target_scope_distinguishes_artifacts_with_the_same_client_key()
    {
        var first = ReferenceReplayExecution.CreateExecutionScope(Guid.NewGuid(), "verify-fix-ui");
        var second = ReferenceReplayExecution.CreateExecutionScope(Guid.NewGuid(), "verify-fix-ui");

        Assert.NotEqual(first, second);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { correlationId = Guid.NewGuid(), successfulOrders = 1 })
            });
    }


    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Keys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            Keys.Add(JsonDocument.Parse(json).RootElement.GetProperty("idempotencyKey").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { correlationId = Guid.NewGuid(), successfulOrders = 1, replayed = false })
            };
        }
    }
}
