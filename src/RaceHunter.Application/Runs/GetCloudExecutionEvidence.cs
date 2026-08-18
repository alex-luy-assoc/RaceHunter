using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Hunts;

namespace RaceHunter.Application.Runs;

public sealed record CloudExecutionEvidence(
    Guid RunId,
    string RunStatus,
    string PlanVersion,
    string WorkerExecution,
    string ModelId,
    string SchemaVersion,
    string ModelInvocationId,
    int TraceEventCount,
    Guid? FindingId,
    string EvidenceCorrelationId);

public sealed class GetCloudExecutionEvidence(
    IRunStore runs,
    IHuntStore hunts,
    ITraceStore traces,
    IAgentIterationReader iterations,
    IFindingStore findings)
{
    public async Task<CloudExecutionEvidence?> ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(runId, cancellationToken);
        var hunt = await hunts.GetByRunAsync(runId, cancellationToken);
        if (run is null || hunt?.Plan is null) return null;
        var traceEvents = await traces.GetAsync(runId, 0, cancellationToken);
        var decisions = await iterations.GetIterationsByRunAsync(runId, cancellationToken);
        var latestDecision = decisions.LastOrDefault();
        var workerExecution = run.Events.LastOrDefault(item => item.Kind == "campaign-started")?.Message ?? string.Empty;
        return new CloudExecutionEvidence(
            run.Id,
            run.Status.ToString(),
            hunt.Plan.PlanVersion,
            workerExecution,
            latestDecision?.ModelId ?? hunt.Plan.ModelId,
            latestDecision?.SchemaVersion ?? hunt.Plan.SchemaVersion,
            latestDecision?.ModelInvocationId ?? hunt.Plan.ModelInvocationId,
            traceEvents.Count,
            await findings.GetIdByRunAsync(runId, cancellationToken),
            traceEvents.FirstOrDefault()?.RequestId ?? $"run:{runId:N}");
    }
}
