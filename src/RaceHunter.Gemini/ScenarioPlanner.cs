using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RaceHunter.Application.Agents;
using RaceHunter.Gemini.Schemas;

namespace RaceHunter.Gemini;

public sealed class ScenarioPlanner(IStructuredModelClient modelClient) : IScenarioPlanner
{
    private const string ModelId = "gemini-3.5-flash";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<ScenarioPlan> PlanAsync(PlanningContext context, CancellationToken cancellationToken)
    {
        if (context.Budget.MaxModelCalls == 0)
            throw new ModelOutputException(ModelOutcome.BudgetExhausted, "model-call budget exhausted");
        var input = BuildInput(context);
        var first = await GenerateCountedAsync(
            new ModelRequest(ModelId, "plan-v1", "plan-v1", input, AgentSchemas.PlanV1, false),
            1,
            cancellationToken);
        if (TryValidate(first, context, 1, out var plan, out var diagnostic)) return plan!;
        if (context.Budget.MaxModelCalls < 2)
            throw new ModelOutputException(ModelOutcome.BudgetExhausted, "model-call budget exhausted before repair", modelCallsConsumed: 1);

        var repairInput = $"{input}\nThe previous JSON was rejected: {diagnostic}. Repair only the JSON and preserve the supplied allowlists and budgets.";
        var repaired = await GenerateCountedAsync(
            new ModelRequest(ModelId, "plan-v1", "plan-v1", repairInput, AgentSchemas.PlanV1, true),
            2,
            cancellationToken);
        if (TryValidate(repaired, context, 2, out plan, out diagnostic)) return plan!;
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

    private static bool TryValidate(ModelResponse response, PlanningContext context, int modelCallsConsumed, out ScenarioPlan? plan, out string diagnostic)
    {
        plan = null;
        try
        {
            var payload = JsonSerializer.Deserialize<PlanPayload>(response.Json, JsonOptions)
                ?? throw new JsonException("empty plan");
            if (payload.SchemaVersion != "plan-v1") throw new JsonException("unsupported schema version");
            if (payload.Actors.Count == 0) throw new JsonException("at least one actor is required");
            var operationIds = context.AllowedOperations.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (payload.Actors.Any(actor => !operationIds.Contains(actor.OperationId))) throw new JsonException("unknown operation");
            if (!context.AllowedInvariantTypes.Contains(payload.Invariant.Type, StringComparer.Ordinal)) throw new JsonException("unknown invariant type");
            if (!context.AllowedStrategies.Contains(payload.Strategy.Kind, StringComparer.Ordinal)) throw new JsonException("unknown strategy");
            if (context.AllowedObservationMetrics is not null && !context.AllowedObservationMetrics.Contains(payload.Invariant.Metric, StringComparer.Ordinal))
                throw new JsonException("unknown observation metric");
            if (payload.Strategy.ActorCount < 1 || payload.Strategy.ActorCount > context.Budget.MaxActors) throw new JsonException("actor count exceeds server budget");
            if (payload.Invariant.Type == "numeric-boundary" && payload.Invariant.Maximum is null) throw new JsonException("numeric boundary maximum is required");
            if (payload.Invariant.Type == "cross-observation" &&
                (string.IsNullOrWhiteSpace(payload.Invariant.LeftMetric) || string.IsNullOrWhiteSpace(payload.Invariant.RightMetric) ||
                 payload.Invariant.Relation is not ("equal" or "less-than-or-equal" or "greater-than-or-equal")))
                throw new JsonException("cross-observation metrics and relation are required");
            if (payload.Invariant.Type == "cross-observation" && context.AllowedObservationMetrics is not null &&
                (!context.AllowedObservationMetrics.Contains(payload.Invariant.LeftMetric!, StringComparer.Ordinal) ||
                 !context.AllowedObservationMetrics.Contains(payload.Invariant.RightMetric!, StringComparer.Ordinal)))
                throw new JsonException("unknown cross-observation metric");

            var validatedJson = JsonSerializer.Serialize(payload, JsonOptions);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(validatedJson))).ToLowerInvariant()[..16];
            plan = new ScenarioPlan(
                $"plan-{hash}",
                payload.SchemaVersion,
                "plan-v1",
                response.ModelId,
                response.InvocationId,
                payload.Actors.Select(item => new PlannedActor(item.Name, item.OperationId)).ToArray(),
                new PlannedInvariant(
                    payload.Invariant.Type,
                    payload.Invariant.Metric,
                    payload.Invariant.Maximum,
                    payload.Invariant.LeftMetric,
                    payload.Invariant.RightMetric,
                    payload.Invariant.Relation),
                new PlannedStrategy(payload.Strategy.Kind, payload.Strategy.ActorCount, payload.Strategy.Seed),
                modelCallsConsumed,
                validatedJson);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            diagnostic = Sanitize(exception.Message);
            return false;
        }
    }

    private static string BuildInput(PlanningContext context)
    {
        var prompt = PromptResources.Read("plan-v1.txt");
        var operations = string.Join(',', context.AllowedOperations.Select(item => $"{item.Id}:{item.Method}:{item.Path}"));
        return $"{prompt}\nobjective={context.Objective}\noperations={operations}\ninvariantTypes={string.Join(',', context.AllowedInvariantTypes)}\nobservationMetrics={string.Join(',', context.AllowedObservationMetrics ?? [])}\nstrategies={string.Join(',', context.AllowedStrategies)}\nmaxActors={context.Budget.MaxActors};maxConcurrency={context.Budget.MaxConcurrentActors};maxRequests={context.Budget.MaxRequests};maxModelCalls={context.Budget.MaxModelCalls};maxDurationSeconds={(int)context.Budget.MaxDuration.TotalSeconds}";
    }

    private static string Sanitize(string message) => message switch
    {
        var value when value.Contains("operation", StringComparison.OrdinalIgnoreCase) => "unknown operation",
        var value when value.Contains("strategy", StringComparison.OrdinalIgnoreCase) => "unknown strategy",
        var value when value.Contains("actor count", StringComparison.OrdinalIgnoreCase) => "actor count exceeds server budget",
        var value when value.Contains("invariant", StringComparison.OrdinalIgnoreCase) => "invalid invariant",
        _ => "response does not match plan-v1"
    };

    private sealed record PlanPayload
    {
        public required string SchemaVersion { get; init; }
        public required List<ActorPayload> Actors { get; init; }
        public required InvariantPayload Invariant { get; init; }
        public required StrategyPayload Strategy { get; init; }
    }

    private sealed record ActorPayload
    {
        public required string Name { get; init; }
        public required string OperationId { get; init; }
    }

    private sealed record InvariantPayload
    {
        public required string Type { get; init; }
        public required string Metric { get; init; }
        public decimal? Maximum { get; init; }
        public string? LeftMetric { get; init; }
        public string? RightMetric { get; init; }
        public string? Relation { get; init; }
    }

    private sealed record StrategyPayload
    {
        public required string Kind { get; init; }
        public required int ActorCount { get; init; }
        public required int Seed { get; init; }
    }
}

internal static class PromptResources
{
    internal static string Read(string fileName)
    {
        var assembly = typeof(PromptResources).Assembly;
        var suffix = $".Prompts.{fileName}";
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Prompt resource is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Trim();
    }
}
