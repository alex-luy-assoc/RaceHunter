using System.Text.Json;
using System.Text.RegularExpressions;
using RaceHunter.Application.Agents;

namespace RaceHunter.Worker.Execution;

internal sealed class DevelopmentModelClient : IStructuredModelClient
{
    public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var json = request.SchemaVersion == "plan-v1"
            ? CreatePlan(request.Input)
            : request.Input.Contains("attempt=1", StringComparison.Ordinal)
                ? """{"schemaVersion":"strategy-v1","action":"repeat","actorCount":2,"strategy":"checkpoint-interleaving","timingAdjustmentMs":0,"rationaleSummary":"Repeat the bounded controlled schedule once."}"""
                : """{"schemaVersion":"strategy-v1","action":"stop","actorCount":2,"strategy":"checkpoint-interleaving","timingAdjustmentMs":0,"rationaleSummary":"Stop after collecting bounded evidence."}""";
        return Task.FromResult(new ModelResponse(json, "deterministic-development-fake", Guid.NewGuid().ToString("N"), null));
    }

    private static string CreatePlan(string input)
    {
        var operations = Value(input, "operations=").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split(':', 2)[0]).Where(value => value.Length > 0).ToArray();
        var metrics = Value(input, "observationMetrics=").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (operations.Length == 0 || metrics.Length == 0)
            throw new ModelOutputException(ModelOutcome.InvalidOutput, "deterministic development contract is empty");
        var objective = Value(input, "objective=");
        var family = Regex.Match(objective, @"invariant-family\s*=\s*(numeric-boundary|cardinality|cross-observation)", RegexOptions.IgnoreCase).Groups[1].Value.ToLowerInvariant();
        if (family.Length == 0) family = "numeric-boundary";
        var maximumMatch = Regex.Match(objective, @"maximum\s*(?:is|=|:)?\s*(-?\d+)", RegexOptions.IgnoreCase);
        var maximum = maximumMatch.Success && decimal.TryParse(maximumMatch.Groups[1].Value, out var parsed) ? parsed : 1m;
        var metric = Directive(objective, "metric", metrics[0]);
        var leftMetric = Directive(objective, "left-metric", metrics[0]);
        var rightMetric = Directive(objective, "right-metric", metrics.Length > 1 ? metrics[1] : metrics[0]);
        var relation = Directive(objective, "relation", "less-than-or-equal");
        var actors = operations.Select((operation, index) => new { name = $"actor-{index + 1}", operationId = operation }).ToArray();
        object invariant = family switch
        {
            "cardinality" => new { type = family, metric },
            "cross-observation" => new { type = family, metric = leftMetric, leftMetric, rightMetric, relation },
            _ => new { type = "numeric-boundary", metric, maximum }
        };
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "plan-v1",
            actors,
            invariant,
            strategy = new { kind = "checkpoint-interleaving", actorCount = Math.Max(2, actors.Length), seed = 42 }
        });
    }

    private static string Value(string input, string prefix) => input.Split('\n')
        .LastOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim() ?? string.Empty;

    private static string Directive(string objective, string name, string fallback)
    {
        var match = Regex.Match(objective, $@"{Regex.Escape(name)}\s*=\s*([a-z][a-z0-9-]{{0,63}})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : fallback;
    }
}
