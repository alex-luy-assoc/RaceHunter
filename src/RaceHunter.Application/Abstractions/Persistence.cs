using RaceHunter.Application.Hunts;
using RaceHunter.Domain.Findings;
using RaceHunter.Domain.Projects;
using RaceHunter.Domain.Replays;
using RaceHunter.Domain.Runs;
using RaceHunter.Domain.Tracing;

namespace RaceHunter.Application.Abstractions;

public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken cancellationToken);
    Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IRunStore
{
    Task AddAsync(ExperimentRun run, CancellationToken cancellationToken);
    Task SaveAsync(ExperimentRun run, CancellationToken cancellationToken);
    Task<ExperimentRun?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<RunEvent>> GetEventsAsync(Guid id, long after, CancellationToken cancellationToken);
    Task<bool> RequestCancellationAsync(Guid id, DateTime requestedAtUtc, CancellationToken cancellationToken);
}

public interface ITraceStore
{
    Task AppendAsync(TraceEvent traceEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<TraceEvent>> GetAsync(Guid runId, long after, CancellationToken cancellationToken);
}

public interface IRunAttemptStore
{
    Task AddAsync(RunAttempt attempt, CancellationToken cancellationToken);
    Task SaveAsync(RunAttempt attempt, CancellationToken cancellationToken);
}

public interface IRunCancellationProbe
{
    Task<DateTime?> GetRequestedAtUtcAsync(Guid runId, CancellationToken cancellationToken);
}

public interface IFindingStore
{
    Task AddAsync(Finding finding, CancellationToken cancellationToken);
    Task<Finding?> GetAsync(Guid findingId, CancellationToken cancellationToken);
    Task<Guid?> GetIdByRunAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
    async Task AddVerifiedAsync(Finding finding, ReplayArtifact artifact, ReplayAttempt vulnerableAttempt, CancellationToken cancellationToken)
    {
        if (this is not IReplayStore replayStore) throw new InvalidOperationException("The finding store cannot persist replay evidence.");
        await replayStore.AddArtifactAsync(artifact, cancellationToken);
        await AddAsync(finding, cancellationToken);
        await replayStore.AddAttemptAsync(vulnerableAttempt, cancellationToken);
    }
}

public interface IReplayStore
{
    Task AddArtifactAsync(ReplayArtifact artifact, CancellationToken cancellationToken);
    Task<ReplayArtifact?> GetArtifactAsync(Guid artifactId, CancellationToken cancellationToken);
    Task AddAttemptAsync(ReplayAttempt attempt, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReplayAttempt>> GetAttemptsAsync(Guid artifactId, CancellationToken cancellationToken);
    async Task<ReplayAttempt> ExecuteFixedOnceAsync(
        Guid artifactId,
        Func<CancellationToken, Task<ReplayAttempt>> execution,
        CancellationToken cancellationToken)
    {
        var existing = (await GetAttemptsAsync(artifactId, cancellationToken))
            .SingleOrDefault(item => item.TargetMode == ReplayTargetMode.Fixed);
        if (existing is not null) return existing;
        var attempt = await execution(cancellationToken);
        await AddAttemptAsync(attempt, cancellationToken);
        return attempt;
    }
}

public interface IAgentIterationReader
{
    Task<IReadOnlyList<AgentIterationRecord>> GetIterationsByRunAsync(Guid runId, CancellationToken cancellationToken);
}
