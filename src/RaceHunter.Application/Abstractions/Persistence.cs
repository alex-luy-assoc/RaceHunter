using RaceHunter.Domain.Projects;
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
