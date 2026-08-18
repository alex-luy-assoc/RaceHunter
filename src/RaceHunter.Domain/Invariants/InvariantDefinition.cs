namespace RaceHunter.Domain.Invariants;

public enum InvariantOutcome
{
    Pass,
    Fail,
    Inconclusive
}

public abstract record InvariantDefinition;
public sealed record NumericBoundaryInvariant(string Metric, decimal Maximum) : InvariantDefinition;
public sealed record CardinalityInvariant(string Metric) : InvariantDefinition;

public enum CrossObservationRelation
{
    Equal,
    LessThanOrEqual,
    GreaterThanOrEqual
}

public sealed record CrossObservationInvariant(string LeftMetric, string RightMetric, CrossObservationRelation Relation) : InvariantDefinition;

public sealed record Observation(string Metric, decimal? NumericValue, string? TextValue, string TraceReference, string? CorrelationKey)
{
    public static Observation Number(string metric, decimal value, string traceReference, string? correlationKey = null) =>
        new(metric, value, null, traceReference, correlationKey);

    public static Observation Text(string metric, string value, string traceReference, string? correlationKey = null) =>
        new(metric, null, value, traceReference, correlationKey);
}

public sealed record InvariantResult(InvariantOutcome Outcome, IReadOnlyList<string> TraceReferences, string Summary);
