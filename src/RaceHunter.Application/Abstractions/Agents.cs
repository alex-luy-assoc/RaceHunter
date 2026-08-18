using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;

namespace RaceHunter.Application.Agents;

public sealed record AllowedTargetOperation(string Id, string Method, string Path);

public sealed record PlanningContext(
    Guid ExperimentId,
    string Objective,
    IReadOnlyList<AllowedTargetOperation> AllowedOperations,
    IReadOnlyList<string> AllowedInvariantTypes,
    IReadOnlyList<string> AllowedStrategies,
    ExperimentBudget Budget,
    IReadOnlyList<string>? AllowedObservationMetrics = null);

public sealed record PlannedActor(string Name, string OperationId);
public sealed record PlannedInvariant(
    string Type,
    string Metric,
    decimal? Maximum,
    string? LeftMetric = null,
    string? RightMetric = null,
    string? Relation = null);
public sealed record PlannedStrategy(string Kind, int ActorCount, int Seed);

public sealed record ScenarioPlan(
    string PlanVersion,
    string SchemaVersion,
    string PromptVersion,
    string ModelId,
    string ModelInvocationId,
    IReadOnlyList<PlannedActor> Actors,
    PlannedInvariant Invariant,
    PlannedStrategy Strategy,
    int ModelCallsConsumed,
    string ValidatedJson);

public enum AgentActionKind
{
    ChangeActorCount,
    SelectStrategy,
    AdjustTiming,
    Repeat,
    StartMinimization,
    Stop
}

public sealed record CampaignSettings(int ActorCount, string Strategy, int TimingAdjustmentMs);
public sealed record DeterministicReplayStep(int ActorId, int OffsetMilliseconds, string OperationId = "place-order");
public sealed record EvidenceSummary(
    string InvariantOutcome,
    IReadOnlyList<string> TraceReferences,
    int AttemptNumber,
    int RequestsConsumed,
    int ModelCallsConsumed = 0,
    IReadOnlyList<DeterministicReplayStep>? Schedule = null,
    CampaignSettings? AttemptSettings = null);

public sealed record StrategySelectionContext(
    Guid ExperimentId,
    CampaignSettings Current,
    EvidenceSummary Evidence,
    IReadOnlyList<string> AllowedStrategies,
    ExperimentBudget Budget,
    int ModelCallsConsumed);

public sealed record StrategyDecision(
    AgentActionKind Action,
    int ActorCount,
    string Strategy,
    int TimingAdjustmentMs,
    string RationaleSummary,
    string SchemaVersion,
    string ModelId,
    string ModelInvocationId,
    int ModelCallsConsumed = 1);

public interface IScenarioPlanner
{
    Task<ScenarioPlan> PlanAsync(PlanningContext context, CancellationToken cancellationToken);
}

public interface IExperimentStrategist
{
    Task<StrategyDecision> SelectNextAsync(StrategySelectionContext context, CancellationToken cancellationToken);
}

public sealed record ModelRequest(
    string ModelId,
    string PromptVersion,
    string SchemaVersion,
    string Input,
    string ResponseSchemaJson,
    bool IsRepair);

public sealed record ModelResponse(string Json, string ModelId, string InvocationId, ModelUsage? Usage);
public sealed record ModelUsage(int? InputTokens, int? OutputTokens);

public interface IStructuredModelClient
{
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken);
}

public enum ModelOutcome
{
    InvalidOutput,
    TransientFailure,
    PermanentFailure,
    BudgetExhausted
}

public sealed class ModelOutputException(ModelOutcome outcome, string sanitizedDiagnostic, Exception? innerException = null, int modelCallsConsumed = 0)
    : Exception($"Model output failed validation: {sanitizedDiagnostic}", innerException)
{
    public ModelOutcome Outcome { get; } = outcome;
    public int ModelCallsConsumed { get; } = modelCallsConsumed;
}

public sealed class AgentActionValidationException(string message) : Exception(message);

public static class PlannedInvariantCompiler
{
    public static InvariantDefinition Compile(PlannedInvariant invariant) => invariant.Type switch
    {
        "numeric-boundary" => new NumericBoundaryInvariant(
            invariant.Metric,
            invariant.Maximum ?? throw new InvalidOperationException("A numeric maximum is required.")),
        "cardinality" => new CardinalityInvariant(invariant.Metric),
        "cross-observation" => new CrossObservationInvariant(
            Required(invariant.LeftMetric, "A left metric is required."),
            Required(invariant.RightMetric, "A right metric is required."),
            invariant.Relation switch
            {
                "equal" => CrossObservationRelation.Equal,
                "less-than-or-equal" => CrossObservationRelation.LessThanOrEqual,
                "greater-than-or-equal" => CrossObservationRelation.GreaterThanOrEqual,
                _ => throw new InvalidOperationException("The cross-observation relation is unsupported.")
            }),
        _ => throw new InvalidOperationException("The approved invariant type is not executable.")
    };

    private static string Required(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value;
}

public static class CampaignBudgetWindow
{
    public static TimeSpan Remaining(DateTime startedAtUtc, TimeSpan maximumDuration, DateTime nowUtc)
    {
        var remaining = maximumDuration - (nowUtc - startedAtUtc);
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}

public static class AgentActionValidator
{
    public static void Validate(StrategyDecision decision, AdaptiveCampaignContext context)
    {
        if (decision.ActorCount < 1 || decision.ActorCount > context.Budget.MaxActors)
            throw new AgentActionValidationException("The proposed actor count exceeds the server-side budget.");
        if (!context.AllowedStrategies.Contains(decision.Strategy, StringComparer.Ordinal))
            throw new AgentActionValidationException("The proposed strategy is not allowlisted.");
        if (decision.TimingAdjustmentMs is < 0 or > 5000)
            throw new AgentActionValidationException("The proposed timing adjustment is outside the server-side bound.");
        if (!string.Equals(decision.SchemaVersion, "strategy-v1", StringComparison.Ordinal))
            throw new AgentActionValidationException("The strategy schema version is unsupported.");
    }
}

public sealed class AdaptiveCampaignContext
{
    public AdaptiveCampaignContext(
        Guid experimentId,
        CampaignSettings initial,
        IReadOnlyList<string> allowedStrategies,
        ExperimentBudget budget,
        int maxIterations,
        int resumeAfterIteration = 0,
        int requestsAlreadyConsumed = 0,
        int modelCallsAlreadyConsumed = 0,
        DeterministicAttemptResult? recoveredAttempt = null,
        int fixedRequestsPerAttempt = 0)
    {
        if (experimentId == Guid.Empty) throw new ArgumentException("An experiment ID is required.", nameof(experimentId));
        if (maxIterations < 1) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (resumeAfterIteration < 0 || resumeAfterIteration > maxIterations) throw new ArgumentOutOfRangeException(nameof(resumeAfterIteration));
        if (requestsAlreadyConsumed < 0 || requestsAlreadyConsumed > budget.MaxRequests) throw new ArgumentOutOfRangeException(nameof(requestsAlreadyConsumed));
        if (modelCallsAlreadyConsumed < 0 || modelCallsAlreadyConsumed > budget.MaxModelCalls) throw new ArgumentOutOfRangeException(nameof(modelCallsAlreadyConsumed));
        if (fixedRequestsPerAttempt < 0 || fixedRequestsPerAttempt > budget.MaxRequests) throw new ArgumentOutOfRangeException(nameof(fixedRequestsPerAttempt));
        ExperimentId = experimentId;
        Initial = initial;
        AllowedStrategies = allowedStrategies;
        Budget = budget;
        MaxIterations = maxIterations;
        ResumeAfterIteration = resumeAfterIteration;
        RequestsAlreadyConsumed = requestsAlreadyConsumed;
        ModelCallsAlreadyConsumed = modelCallsAlreadyConsumed;
        RecoveredAttempt = recoveredAttempt;
        FixedRequestsPerAttempt = fixedRequestsPerAttempt;
    }

    public Guid ExperimentId { get; }
    public CampaignSettings Initial { get; }
    public IReadOnlyList<string> AllowedStrategies { get; }
    public ExperimentBudget Budget { get; }
    public int MaxIterations { get; }
    public int ResumeAfterIteration { get; }
    public int RequestsAlreadyConsumed { get; }
    public int ModelCallsAlreadyConsumed { get; }
    public DeterministicAttemptResult? RecoveredAttempt { get; }
    public int FixedRequestsPerAttempt { get; }
}

public sealed record DeterministicAttemptResult(
    InvariantOutcome InvariantOutcome,
    IReadOnlyList<string> TraceReferences,
    int RequestsConsumed = 0,
    IReadOnlyList<DeterministicReplayStep>? Schedule = null);

public enum CampaignOutcome
{
    VerifiedViolation,
    CompletedWithoutFinding,
    BudgetExhausted,
    ModelFailed,
    WorkerFailed
}

public sealed record AdaptiveCampaignResult(
    CampaignOutcome Outcome,
    bool VerifiedViolation,
    int Attempts,
    int ModelCalls,
    IReadOnlyList<StrategyDecision> Decisions,
    CampaignSettings? FailedSettings = null,
    DeterministicAttemptResult? FailedAttempt = null,
    int RequestsConsumed = 0);

public sealed class AdaptiveStrategyLoop(IExperimentStrategist strategist)
{
    public async Task<AdaptiveCampaignResult> RunAsync(
        AdaptiveCampaignContext context,
        Func<int, CampaignSettings, CancellationToken, Task<DeterministicAttemptResult>> executeAttempt,
        CancellationToken cancellationToken,
        Func<int, StrategyDecision, EvidenceSummary, CancellationToken, Task>? decisionSink = null,
        Func<int, CampaignSettings, EvidenceSummary, CancellationToken, Task>? attemptSink = null)
    {
        var settings = context.Initial;
        var decisions = new List<StrategyDecision>();
        var verified = false;
        var requestsConsumed = context.RequestsAlreadyConsumed;
        var modelCallsConsumed = context.ModelCallsAlreadyConsumed;
        CampaignSettings? failedSettings = null;
        DeterministicAttemptResult? failedAttempt = null;
        for (var iteration = context.ResumeAfterIteration + 1; iteration <= context.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeterministicAttemptResult attempt;
            var recovered = iteration == context.ResumeAfterIteration + 1 && context.RecoveredAttempt is not null;
            if (recovered)
            {
                attempt = context.RecoveredAttempt!;
            }
            else
            {
                var attemptCost = checked(settings.ActorCount + context.FixedRequestsPerAttempt);
                if (requestsConsumed >= context.Budget.MaxRequests || attemptCost > context.Budget.MaxRequests - requestsConsumed)
                    return new AdaptiveCampaignResult(CampaignOutcome.BudgetExhausted, verified, iteration - 1, modelCallsConsumed, decisions, RequestsConsumed: requestsConsumed);
                attempt = await executeAttempt(iteration, settings, cancellationToken);
                requestsConsumed = checked(requestsConsumed + attempt.RequestsConsumed);
            }
            verified |= attempt.InvariantOutcome == InvariantOutcome.Fail;
            if (attempt.InvariantOutcome == InvariantOutcome.Fail)
            {
                failedSettings = settings;
                failedAttempt = attempt;
            }

            var evidence = new EvidenceSummary(attempt.InvariantOutcome.ToString(), attempt.TraceReferences, iteration, requestsConsumed, modelCallsConsumed, attempt.Schedule, settings);
            if (!recovered && attemptSink is not null) await attemptSink(iteration, settings, evidence, cancellationToken);

            if (modelCallsConsumed >= context.Budget.MaxModelCalls)
                return new AdaptiveCampaignResult(CampaignOutcome.BudgetExhausted, verified, iteration, modelCallsConsumed, decisions, RequestsConsumed: requestsConsumed);

            StrategyDecision decision;
            try
            {
                decision = await strategist.SelectNextAsync(new StrategySelectionContext(
                    context.ExperimentId,
                    settings,
                    evidence,
                    context.AllowedStrategies,
                    context.Budget,
                    modelCallsConsumed), cancellationToken);
                AgentActionValidator.Validate(decision, context);
            }
            catch (ModelOutputException exception) when (exception.Outcome == ModelOutcome.TransientFailure)
            {
                decision = new StrategyDecision(
                    AgentActionKind.Repeat,
                    settings.ActorCount,
                    settings.Strategy,
                    settings.TimingAdjustmentMs,
                    "Deterministic fallback repeats the last bounded strategy after a transient model failure.",
                    "strategy-v1",
                    "deterministic-fallback",
                    $"fallback-{iteration}",
                    exception.ModelCallsConsumed);
            }
            catch (ModelOutputException exception) when (exception.Outcome == ModelOutcome.BudgetExhausted)
            {
                return new AdaptiveCampaignResult(CampaignOutcome.BudgetExhausted, verified, iteration, checked(modelCallsConsumed + exception.ModelCallsConsumed), decisions, RequestsConsumed: requestsConsumed);
            }
            catch (ModelOutputException exception)
            {
                return new AdaptiveCampaignResult(CampaignOutcome.ModelFailed, verified, iteration, checked(modelCallsConsumed + exception.ModelCallsConsumed), decisions, RequestsConsumed: requestsConsumed);
            }
            catch (AgentActionValidationException)
            {
                return new AdaptiveCampaignResult(CampaignOutcome.ModelFailed, verified, iteration, modelCallsConsumed, decisions, RequestsConsumed: requestsConsumed);
            }

            if (modelCallsConsumed + decision.ModelCallsConsumed > context.Budget.MaxModelCalls)
                return new AdaptiveCampaignResult(CampaignOutcome.BudgetExhausted, verified, iteration, modelCallsConsumed, decisions, RequestsConsumed: requestsConsumed);
            decisions.Add(decision);
            modelCallsConsumed += decision.ModelCallsConsumed;
            evidence = evidence with { ModelCallsConsumed = modelCallsConsumed };
            if (decisionSink is not null) await decisionSink(iteration, decision, evidence, cancellationToken);
            if (decision.Action is AgentActionKind.Stop or AgentActionKind.StartMinimization)
            {
                var outcome = verified ? CampaignOutcome.VerifiedViolation : CampaignOutcome.CompletedWithoutFinding;
                return new AdaptiveCampaignResult(outcome, verified, iteration, modelCallsConsumed, decisions, failedSettings, failedAttempt, requestsConsumed);
            }

            settings = new CampaignSettings(decision.ActorCount, decision.Strategy, decision.TimingAdjustmentMs);
        }

        return new AdaptiveCampaignResult(CampaignOutcome.BudgetExhausted, verified, context.MaxIterations, modelCallsConsumed, decisions, RequestsConsumed: requestsConsumed);
    }
}
