using RaceHunter.Domain.Invariants;

namespace RaceHunter.Concurrency.Invariants;

public sealed class NumericBoundaryEvaluator
{
    public InvariantResult Evaluate(NumericBoundaryInvariant definition, IReadOnlyCollection<Observation> observations)
    {
        var relevant = observations.Where(item => item.Metric == definition.Metric && item.NumericValue.HasValue).ToArray();
        if (relevant.Length == 0)
            return new InvariantResult(InvariantOutcome.Inconclusive, [], "The required numeric metric was not observed.");
        var observed = relevant.Max(item => item.NumericValue!.Value);
        return new InvariantResult(
            observed <= definition.Maximum ? InvariantOutcome.Pass : InvariantOutcome.Fail,
            relevant.Select(item => item.TraceReference).Distinct(StringComparer.Ordinal).ToArray(),
            $"Observed {definition.Metric}={observed}; maximum={definition.Maximum}.");
    }
}
