using System.Text.Json;
using System.Text.Json.Serialization;
using RaceHunter.Application.Agents;
using RaceHunter.Gemini.Schemas;

namespace RaceHunter.Gemini;

public sealed class ExperimentStrategist(IStructuredModelClient modelClient) : IExperimentStrategist
{
    private const string ModelId = "gemini-3.5-flash";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<StrategyDecision> SelectNextAsync(StrategySelectionContext context, CancellationToken cancellationToken)
    {
        if (context.ModelCallsConsumed >= context.Budget.MaxModelCalls)
            throw new ModelOutputException(ModelOutcome.BudgetExhausted, "model-call budget exhausted");
        var input = BuildInput(context);
        var first = await GenerateCountedAsync(
            new ModelRequest(ModelId, "strategy-v1", "strategy-v1", input, AgentSchemas.StrategyV1, false),
            1,
            cancellationToken);
        if (TryValidate(first, context, 1, out var decision, out var diagnostic)) return decision!;
        if (context.ModelCallsConsumed + 1 >= context.Budget.MaxModelCalls)
            throw new ModelOutputException(ModelOutcome.BudgetExhausted, "model-call budget exhausted before repair", modelCallsConsumed: 1);
        var repaired = await GenerateCountedAsync(
            new ModelRequest(ModelId, "strategy-v1", "strategy-v1", $"{input}\nThe previous JSON was rejected: {diagnostic}. Repair only the JSON.", AgentSchemas.StrategyV1, true),
            2,
            cancellationToken);
        if (TryValidate(repaired, context, 2, out decision, out diagnostic)) return decision!;
        throw new ModelOutputException(ModelOutcome.InvalidOutput, diagnostic, modelCallsConsumed: 2);
    }

    private async Task<ModelResponse> GenerateCountedAsync(ModelRequest request, int callsConsumed, CancellationToken cancellationToken)
    {
        try
        {
            return await modelClient.GenerateAsync(request, cancellationToken);
        }
        catch (ModelOutputException exception)
        {
            throw new ModelOutputException(exception.Outcome, "provider invocation failed", exception, callsConsumed);
        }
    }

    private static bool TryValidate(ModelResponse response, StrategySelectionContext context, int modelCallsConsumed, out StrategyDecision? decision, out string diagnostic)
    {
        decision = null;
        try
        {
            var payload = JsonSerializer.Deserialize<StrategyPayload>(response.Json, JsonOptions)
                ?? throw new JsonException("empty strategy");
            if (payload.SchemaVersion != "strategy-v1") throw new JsonException("unsupported strategy schema");
            var action = payload.Action switch
            {
                "change-actor-count" => AgentActionKind.ChangeActorCount,
                "select-strategy" => AgentActionKind.SelectStrategy,
                "adjust-timing" => AgentActionKind.AdjustTiming,
                "repeat" => AgentActionKind.Repeat,
                "start-minimization" => AgentActionKind.StartMinimization,
                "stop" => AgentActionKind.Stop,
                _ => throw new JsonException("unknown action")
            };
            if (!context.AllowedStrategies.Contains(payload.Strategy, StringComparer.Ordinal)) throw new JsonException("unknown strategy");
            if (payload.ActorCount < 1 || payload.ActorCount > context.Budget.MaxActors) throw new JsonException("actor count exceeds server budget");
            if (payload.TimingAdjustmentMs is < 0 or > 5000) throw new JsonException("timing exceeds server bound");
            decision = new StrategyDecision(action, payload.ActorCount, payload.Strategy, payload.TimingAdjustmentMs, payload.RationaleSummary, payload.SchemaVersion, response.ModelId, response.InvocationId, modelCallsConsumed);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            diagnostic = exception.Message switch
            {
                var value when value.Contains("strategy", StringComparison.OrdinalIgnoreCase) => "unknown strategy",
                var value when value.Contains("actor count", StringComparison.OrdinalIgnoreCase) => "actor count exceeds server budget",
                var value when value.Contains("timing", StringComparison.OrdinalIgnoreCase) => "timing exceeds server bound",
                _ => "response does not match strategy-v1"
            };
            return false;
        }
    }

    private static string BuildInput(StrategySelectionContext context) =>
        $"{PromptResources.Read("strategy-v1.txt")}\ncurrentActors={context.Current.ActorCount};currentStrategy={context.Current.Strategy};currentTimingMs={context.Current.TimingAdjustmentMs}\nevidenceOutcome={context.Evidence.InvariantOutcome};attempt={context.Evidence.AttemptNumber};requests={context.Evidence.RequestsConsumed};traceReferences={string.Join(',', context.Evidence.TraceReferences)}\nstrategies={string.Join(',', context.AllowedStrategies)}\nmaxActors={context.Budget.MaxActors};maxRequests={context.Budget.MaxRequests};maxModelCalls={context.Budget.MaxModelCalls}";

    private sealed record StrategyPayload
    {
        public required string SchemaVersion { get; init; }
        public required string Action { get; init; }
        public required int ActorCount { get; init; }
        public required string Strategy { get; init; }
        public required int TimingAdjustmentMs { get; init; }
        public required string RationaleSummary { get; init; }
    }
}
