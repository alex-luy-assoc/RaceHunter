using RaceHunter.Application.Abstractions;
using RaceHunter.Domain.Runs;
using RaceHunter.Domain.Tracing;

namespace RaceHunter.Application.Runs;

public sealed class GetRun(IRunStore runStore, ITraceStore traceStore)
{
    public Task<ExperimentRun?> ExecuteAsync(Guid id, CancellationToken cancellationToken) =>
        runStore.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<RunEvent>> GetEventsAsync(Guid id, long after, CancellationToken cancellationToken) =>
        runStore.GetEventsAsync(id, Math.Max(0, after), cancellationToken);

    public Task<IReadOnlyList<TraceEvent>> GetTracesAsync(Guid id, long after, CancellationToken cancellationToken) =>
        traceStore.GetAsync(id, Math.Max(0, after), cancellationToken);
}
