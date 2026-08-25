using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RaceHunter.Application.Agents;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using RaceHunter.Worker.Execution;
using Xunit;

namespace RaceHunter.Worker.Tests;

public sealed class ReferenceInventoryTargetClientTests
{
    [Fact]
    public void Reference_target_timeout_allows_a_cloud_run_cold_start_within_the_campaign_budget()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), ReferenceInventoryTargetClient.RequestTimeout);
        Assert.True(ReferenceInventoryTargetClient.RequestTimeout < TimeSpan.FromSeconds(90));
    }

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
    public async Task Global_inventory_snapshot_detects_oversell_hidden_by_per_response_snapshots()
    {
        var handler = new OversellSnapshotHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://reference-target") };
        var client = new ReferenceInventoryTargetClient(http);
        var observations = new List<Observation>();
        for (var actor = 1; actor <= 5; actor++)
        {
            var response = await client.PlaceOrderAsync(Guid.NewGuid(), new ScheduledActor(actor, TimeSpan.Zero), CancellationToken.None);
            observations.AddRange(response.Observations);
        }
        var snapshot = await client.GetInventorySnapshotAsync(CancellationToken.None);

        var selected = ReferenceCampaignAttemptExecutor.SelectReferenceInvariantObservations(observations, snapshot.Observations);
        var invariant = new CrossObservationEvaluator().Evaluate(
            new CrossObservationInvariant("successful-orders", "inventory-capacity", CrossObservationRelation.LessThanOrEqual),
            selected);

        Assert.Equal(InvariantOutcome.Fail, invariant.Outcome);
        Assert.Single(selected, item => item.Metric == "successful-orders");
        Assert.Single(selected, item => item.Metric == "inventory-capacity");
        Assert.Equal(1, handler.InventoryReads);
    }

    [Fact]
    public async Task Replay_budget_reserves_the_global_inventory_snapshot_request()
    {
        var handler = new OrderStatusHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://reference-target") };
        var client = new ReferenceInventoryTargetClient(http);
        var candidate = new ReplayCandidate("simultaneous-start", 1,
            [new ReplayStep(1, "place-order", "place-order", 0), new ReplayStep(2, "place-order", "place-order", 0)]);

        var requests = await client.CountMissingOrdersAsync(candidate, "probe", "demo-key", new HashSet<string>(), CancellationToken.None);

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Campaign_budget_reserves_the_global_inventory_snapshot_before_actor_requests()
    {
        var budget = new RaceHunter.Domain.Budgets.ExperimentBudget(10, 10, 10, 1, TimeSpan.FromSeconds(30), 0);
        var context = new AdaptiveCampaignContext(Guid.NewGuid(), new CampaignSettings(10, "simultaneous-start", 0),
            ["simultaneous-start"], budget, 1, fixedRequestsPerAttempt: ReferenceCampaignAttemptExecutor.ReferenceSnapshotRequestsPerAttempt);
        var calls = 0;

        var result = await new AdaptiveStrategyLoop(new StubStrategist()).RunAsync(
            context,
            (_, _, _) => { calls++; return Task.FromResult(new DeterministicAttemptResult(InvariantOutcome.Pass, [])); },
            CancellationToken.None);

        Assert.Equal(CampaignOutcome.BudgetExhausted, result.Outcome);
        Assert.Equal(0, calls);
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

    [Fact]
    public void Maximum_caller_replay_key_derives_a_fixed_length_receiver_safe_scope()
    {
        var scope = ReferenceReplayExecution.CreateExecutionScope(Guid.NewGuid(), new string('x', 160));

        Assert.Equal(71, scope.Length);
        Assert.StartsWith("replay:", scope, StringComparison.Ordinal);
        Assert.True($"{scope}:1:setup".Length <= 160);
    }

    [Fact]
    public void Manual_receiver_operation_key_is_fixed_length_for_long_internal_scope_and_operation()
    {
        var key = ManualHttpTargetClient.CreateReceiverOperationKey(
            $"{Guid.NewGuid():N}:minimize:step:1:{new string('f', 64)}", 100, new string('s', 64));

        Assert.Equal(67, key.Length);
        Assert.StartsWith("op:", key, StringComparison.Ordinal);
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

    private sealed class OversellSnapshotHandler : HttpMessageHandler
    {
        public int InventoryReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/inventory")
            {
                InventoryReads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { available = 0, successfulOrders = 5, mode = "vulnerable" })
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { correlationId = Guid.NewGuid(), successfulOrders = 1, replayed = false })
            });
        }
    }

    private sealed class OrderStatusHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { missing = 0, completed = Array.Empty<object>() })
            });
    }

    private sealed class StubStrategist : IExperimentStrategist
    {
        public Task<StrategyDecision> SelectNextAsync(StrategySelectionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new StrategyDecision(AgentActionKind.Stop, 10, "simultaneous-start", 0, "Done.", "strategy-v1", "fake", "i-1"));
    }
}
