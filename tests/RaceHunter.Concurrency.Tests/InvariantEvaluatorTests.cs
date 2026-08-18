using RaceHunter.Concurrency.Invariants;
using RaceHunter.Domain.Invariants;
using Xunit;

namespace RaceHunter.Concurrency.Tests;

public sealed class InvariantEvaluatorTests
{
    [Theory]
    [InlineData(2, 1, InvariantOutcome.Fail)]
    [InlineData(1, 1, InvariantOutcome.Pass)]
    public void Numeric_boundary_compares_observed_value(decimal observed, decimal maximum, InvariantOutcome expected)
    {
        var result = new NumericBoundaryEvaluator().Evaluate(new NumericBoundaryInvariant("successful-orders", maximum),
            [Observation.Number("successful-orders", observed, "trace-1")]);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(["trace-1"], result.TraceReferences);
    }

    [Fact]
    public void Numeric_boundary_is_inconclusive_when_metric_is_absent() =>
        Assert.Equal(InvariantOutcome.Inconclusive,
            new NumericBoundaryEvaluator().Evaluate(new NumericBoundaryInvariant("successful-orders", 1), []).Outcome);

    [Theory]
    [InlineData(new[] { "order-1", "order-1" }, InvariantOutcome.Fail)]
    [InlineData(new[] { "order-1", "order-2" }, InvariantOutcome.Pass)]
    public void Cardinality_requires_unique_values(string[] values, InvariantOutcome expected)
    {
        var observations = values.Select((value, index) => Observation.Text("order-id", value, $"trace-{index}")).ToArray();

        Assert.Equal(expected, new CardinalityEvaluator().Evaluate(new CardinalityInvariant("order-id"), observations).Outcome);
    }

    [Fact]
    public void Cardinality_is_inconclusive_without_values() =>
        Assert.Equal(InvariantOutcome.Inconclusive,
            new CardinalityEvaluator().Evaluate(new CardinalityInvariant("order-id"), []).Outcome);

    [Theory]
    [InlineData(1, 1, InvariantOutcome.Pass)]
    [InlineData(1, 0, InvariantOutcome.Fail)]
    public void Cross_observation_compares_two_metrics(decimal left, decimal right, InvariantOutcome expected)
    {
        var observations = new[]
        {
            Observation.Number("remaining-from-response", left, "trace-a"),
            Observation.Number("remaining-from-state", right, "trace-b")
        };

        Assert.Equal(expected, new CrossObservationEvaluator().Evaluate(
            new CrossObservationInvariant("remaining-from-response", "remaining-from-state", CrossObservationRelation.Equal), observations).Outcome);
    }

    [Fact]
    public void Cross_observation_is_inconclusive_when_either_side_is_absent() =>
        Assert.Equal(InvariantOutcome.Inconclusive, new CrossObservationEvaluator().Evaluate(
            new CrossObservationInvariant("left", "right", CrossObservationRelation.Equal),
            [Observation.Number("left", 1, "trace-a")]).Outcome);

    [Fact]
    public void Cross_observation_is_inconclusive_for_multiple_uncorrelated_pairs()
    {
        var observations = new[]
        {
            Observation.Number("left", 1, "trace-a"),
            Observation.Number("left", 2, "trace-b"),
            Observation.Number("right", 1, "trace-c"),
            Observation.Number("right", 2, "trace-d")
        };

        Assert.Equal(InvariantOutcome.Inconclusive, new CrossObservationEvaluator().Evaluate(
            new CrossObservationInvariant("left", "right", CrossObservationRelation.Equal), observations).Outcome);
    }

    [Fact]
    public void Cross_observation_is_inconclusive_when_any_correlated_observation_is_unpaired()
    {
        var observations = new[]
        {
            Observation.Number("left", 1, "trace-a", "A"),
            Observation.Number("left", 2, "trace-b", "B"),
            Observation.Number("right", 1, "trace-c", "A")
        };

        Assert.Equal(InvariantOutcome.Inconclusive, new CrossObservationEvaluator().Evaluate(
            new CrossObservationInvariant("left", "right", CrossObservationRelation.Equal), observations).Outcome);
    }

    [Fact]
    public void Cross_observation_is_inconclusive_for_mismatched_single_pair_keys()
    {
        var observations = new[]
        {
            Observation.Number("left", 1, "trace-a", "A"),
            Observation.Number("right", 2, "trace-b", "B")
        };

        Assert.Equal(InvariantOutcome.Inconclusive, new CrossObservationEvaluator().Evaluate(
            new CrossObservationInvariant("left", "right", CrossObservationRelation.Equal), observations).Outcome);
    }
}
