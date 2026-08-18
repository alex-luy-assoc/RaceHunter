using RaceHunter.Application.Agents;
using RaceHunter.Domain.Budgets;
using RaceHunter.Gemini;
using Xunit;

namespace RaceHunter.Gemini.Tests;

public sealed class GeminiPlanningTests
{
    private static readonly PlanningContext Context = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "Successful orders must not exceed inventory",
        [new AllowedTargetOperation("place-order", "POST", "/api/orders")],
        ["numeric-boundary"],
        ["simultaneous-start", "seeded-jitter", "checkpoint-interleaving"],
        ExperimentBudget.PublicSandbox,
        ["successful-orders", "inventory-capacity", "order-correlation"]);

    [Fact]
    public async Task Planning_input_names_objective_operations_and_server_budgets()
    {
        var model = new ScriptedModelClient(ValidPlanJson());
        await new ScenarioPlanner(model).PlanAsync(Context, CancellationToken.None);

        var input = Assert.Single(model.Requests).Input;
        Assert.Contains(Context.Objective, input, StringComparison.Ordinal);
        Assert.Contains("place-order", input, StringComparison.Ordinal);
        Assert.Contains("maxActors=10", input, StringComparison.Ordinal);
        Assert.Contains("maxRequests=40", input, StringComparison.Ordinal);
        Assert.Contains("maxModelCalls=5", input, StringComparison.Ordinal);
        Assert.Contains("inventory-capacity", input, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valid_plan_is_versioned_from_validated_content_and_records_model_contract()
    {
        var plan = await new ScenarioPlanner(new ScriptedModelClient(ValidPlanJson())).PlanAsync(Context, CancellationToken.None);

        Assert.StartsWith("plan-", plan.PlanVersion, StringComparison.Ordinal);
        Assert.Equal("plan-v1", plan.SchemaVersion);
        Assert.Equal("plan-v1", plan.PromptVersion);
        Assert.Equal("gemini-3.5-flash", plan.ModelId);
    }

    [Fact]
    public async Task Unknown_operation_receives_one_constrained_repair_attempt()
    {
        var model = new ScriptedModelClient(ValidPlanJson("delete-everything"), ValidPlanJson());
        var plan = await new ScenarioPlanner(model).PlanAsync(Context, CancellationToken.None);

        Assert.Equal("place-order", Assert.Single(plan.Actors).OperationId);
        Assert.Equal(2, model.Requests.Count);
        Assert.True(model.Requests[1].IsRepair);
        Assert.Contains("unknown operation", model.Requests[1].Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_invalid_plan_outputs_become_explicit_model_failure()
    {
        var model = new ScriptedModelClient("{}", "{}");

        var error = await Assert.ThrowsAsync<ModelOutputException>(() =>
            new ScenarioPlanner(model).PlanAsync(Context, CancellationToken.None));

        Assert.Equal(ModelOutcome.InvalidOutput, error.Outcome);
        Assert.Equal(2, error.ModelCallsConsumed);
        Assert.Equal(2, model.Requests.Count);
        Assert.DoesNotContain("Successful orders", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Planner_honors_zero_model_budget_without_invoking_provider()
    {
        var model = new ScriptedModelClient(ValidPlanJson());
        var noModelBudget = Context with { Budget = new ExperimentBudget(2, 2, 4, 0, TimeSpan.FromSeconds(10), 0) };

        var error = await Assert.ThrowsAsync<ModelOutputException>(() => new ScenarioPlanner(model).PlanAsync(noModelBudget, CancellationToken.None));

        Assert.Equal(ModelOutcome.BudgetExhausted, error.Outcome);
        Assert.Equal(0, error.ModelCallsConsumed);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Planner_rejects_unknown_strategy_after_repair()
    {
        var model = new ScriptedModelClient(ValidPlanJson(strategy: "unbounded-flood"), ValidPlanJson(strategy: "unbounded-flood"));
        await Assert.ThrowsAsync<ModelOutputException>(() => new ScenarioPlanner(model).PlanAsync(Context, CancellationToken.None));
    }

    [Fact]
    public async Task Planner_rejects_actor_count_above_server_budget_after_repair()
    {
        var model = new ScriptedModelClient(ValidPlanJson(actorCount: 11), ValidPlanJson(actorCount: 11));
        await Assert.ThrowsAsync<ModelOutputException>(() => new ScenarioPlanner(model).PlanAsync(Context, CancellationToken.None));
    }

    [Fact]
    public async Task Strategist_accepts_allowlisted_evidence_grounded_action()
    {
        var model = new ScriptedModelClient("""{"schemaVersion":"strategy-v1","action":"repeat","actorCount":2,"strategy":"seeded-jitter","timingAdjustmentMs":5,"rationaleSummary":"One more bounded sample."}""");
        var decision = await new ExperimentStrategist(model).SelectNextAsync(StrategyContext(), CancellationToken.None);

        Assert.Equal(AgentActionKind.Repeat, decision.Action);
        Assert.Equal("seeded-jitter", decision.Strategy);
    }

    [Fact]
    public async Task Strategist_rejects_action_that_exceeds_actor_budget()
    {
        var json = """{"schemaVersion":"strategy-v1","action":"change-actor-count","actorCount":50,"strategy":"simultaneous-start","timingAdjustmentMs":0,"rationaleSummary":"Try more."}""";
        var model = new ScriptedModelClient(json, json);
        await Assert.ThrowsAsync<ModelOutputException>(() => new ExperimentStrategist(model).SelectNextAsync(StrategyContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Cross_observation_plan_requires_executable_metrics_and_relation()
    {
        var json = """{"schemaVersion":"plan-v1","actors":[{"name":"buyer","operationId":"place-order"}],"invariant":{"type":"cross-observation","metric":"successful-orders","maximum":null,"leftMetric":"successful-orders","rightMetric":"inventory-capacity","relation":"less-than-or-equal"},"strategy":{"kind":"simultaneous-start","actorCount":2,"seed":42}}""";
        var plan = await new ScenarioPlanner(new ScriptedModelClient(json)).PlanAsync(Context with
        {
            AllowedInvariantTypes = ["numeric-boundary", "cardinality", "cross-observation"],
            AllowedObservationMetrics = ["successful-orders", "inventory-capacity", "order-correlation"]
        }, CancellationToken.None);

        Assert.Equal("successful-orders", plan.Invariant.LeftMetric);
        Assert.Equal("inventory-capacity", plan.Invariant.RightMetric);
        Assert.Equal("less-than-or-equal", plan.Invariant.Relation);
    }

    private static StrategySelectionContext StrategyContext() => new(
        Context.ExperimentId,
        new CampaignSettings(2, "simultaneous-start", 0),
        new EvidenceSummary("Pass", ["trace:1"], 1, 2),
        Context.AllowedStrategies,
        Context.Budget,
        1);

    private static string ValidPlanJson(string operation = "place-order", string strategy = "simultaneous-start", int actorCount = 2) =>
        $"{{\"schemaVersion\":\"plan-v1\",\"actors\":[{{\"name\":\"buyer\",\"operationId\":\"{operation}\"}}],\"invariant\":{{\"type\":\"numeric-boundary\",\"metric\":\"successful-orders\",\"maximum\":1}},\"strategy\":{{\"kind\":\"{strategy}\",\"actorCount\":{actorCount},\"seed\":42}}}}";

    private sealed class ScriptedModelClient(params string[] responses) : IStructuredModelClient
    {
        private int index;
        public List<ModelRequest> Requests { get; } = [];

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = responses[Math.Min(index++, responses.Length - 1)];
            return Task.FromResult(new ModelResponse(response, "gemini-3.5-flash", "invocation-test", null));
        }
    }
}
