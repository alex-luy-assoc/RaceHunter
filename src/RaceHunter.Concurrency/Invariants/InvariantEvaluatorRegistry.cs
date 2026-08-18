using RaceHunter.Domain.Invariants;

namespace RaceHunter.Concurrency.Invariants;

public sealed class InvariantEvaluatorRegistry
{
    public InvariantResult Evaluate(InvariantDefinition definition, IReadOnlyCollection<Observation> observations) => definition switch
    {
        NumericBoundaryInvariant numeric => new NumericBoundaryEvaluator().Evaluate(numeric, observations),
        CardinalityInvariant cardinality => new CardinalityEvaluator().Evaluate(cardinality, observations),
        CrossObservationInvariant crossObservation => new CrossObservationEvaluator().Evaluate(crossObservation, observations),
        _ => throw new ArgumentOutOfRangeException(nameof(definition))
    };
}
