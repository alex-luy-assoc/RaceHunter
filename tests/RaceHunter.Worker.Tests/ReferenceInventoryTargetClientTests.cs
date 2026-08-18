using System.Net;
using System.Net.Http.Json;
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

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { correlationId = Guid.NewGuid(), successfulOrders = 1 })
            });
    }
}
