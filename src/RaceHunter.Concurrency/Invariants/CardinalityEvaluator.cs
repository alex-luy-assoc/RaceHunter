using RaceHunter.Domain.Invariants;

namespace RaceHunter.Concurrency.Invariants;

public sealed class CardinalityEvaluator
{
    public InvariantResult Evaluate(CardinalityInvariant definition, IReadOnlyCollection<Observation> observations)
    {
        var relevant = observations.Where(item => item.Metric == definition.Metric && item.TextValue is not null).ToArray();
        if (relevant.Length == 0)
            return new InvariantResult(InvariantOutcome.Inconclusive, [], "The required cardinality values were not observed.");
        var distinct = relevant.Select(item => item.TextValue!).Distinct(StringComparer.Ordinal).Count();
        return new InvariantResult(
            distinct == relevant.Length ? InvariantOutcome.Pass : InvariantOutcome.Fail,
            relevant.Select(item => item.TraceReference).Distinct(StringComparer.Ordinal).ToArray(),
            $"Observed {relevant.Length} values and {distinct} distinct values for {definition.Metric}.");
    }
}
