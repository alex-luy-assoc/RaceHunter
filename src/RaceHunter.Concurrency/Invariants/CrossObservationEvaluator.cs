using RaceHunter.Domain.Invariants;

namespace RaceHunter.Concurrency.Invariants;

public sealed class CrossObservationEvaluator
{
    public InvariantResult Evaluate(CrossObservationInvariant definition, IReadOnlyCollection<Observation> observations)
    {
        var left = observations.Where(item => item.Metric == definition.LeftMetric && item.NumericValue.HasValue).ToArray();
        var right = observations.Where(item => item.Metric == definition.RightMetric && item.NumericValue.HasValue).ToArray();
        if (left.Length == 0 || right.Length == 0)
            return new InvariantResult(InvariantOutcome.Inconclusive, [], "Both related observations are required.");

        var pairs = PairCorrelated(left, right);
        if (pairs.Count == 0)
            return new InvariantResult(InvariantOutcome.Inconclusive, [], "Related observations could not be correlated unambiguously.");
        var passed = pairs.All(pair => Compare(definition.Relation, pair.Left.NumericValue!.Value, pair.Right.NumericValue!.Value));
        return new InvariantResult(
            passed ? InvariantOutcome.Pass : InvariantOutcome.Fail,
            pairs.SelectMany(pair => new[] { pair.Left.TraceReference, pair.Right.TraceReference }).Distinct(StringComparer.Ordinal).ToArray(),
            $"Compared {pairs.Count} correlated {definition.LeftMetric}/{definition.RightMetric} observation pair(s) using {definition.Relation}.");
    }

    private static IReadOnlyList<(Observation Left, Observation Right)> PairCorrelated(
        IReadOnlyCollection<Observation> left,
        IReadOnlyCollection<Observation> right)
    {
        if (left.Count == 1 && right.Count == 1)
        {
            var singleLeft = left.Single();
            var singleRight = right.Single();
            return singleLeft.CorrelationKey == singleRight.CorrelationKey ? [(singleLeft, singleRight)] : [];
        }
        if (left.Any(item => item.CorrelationKey is null) || right.Any(item => item.CorrelationKey is null)) return [];
        var leftGroups = left.GroupBy(item => item.CorrelationKey!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var rightGroups = right.GroupBy(item => item.CorrelationKey!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (leftGroups.Count != rightGroups.Count || leftGroups.Keys.Except(rightGroups.Keys, StringComparer.Ordinal).Any()) return [];

        var pairs = new List<(Observation, Observation)>();
        foreach (var key in leftGroups.Keys)
        {
            var leftForKey = leftGroups[key];
            var rightForKey = rightGroups[key];
            if (leftForKey.Length == 1 && rightForKey.Length == 1) pairs.Add((leftForKey[0], rightForKey[0]));
            else return [];
        }
        return pairs;
    }

    private static bool Compare(CrossObservationRelation relation, decimal left, decimal right) => relation switch
    {
        CrossObservationRelation.Equal => left == right,
        CrossObservationRelation.LessThanOrEqual => left <= right,
        CrossObservationRelation.GreaterThanOrEqual => left >= right,
        _ => false
    };
}
