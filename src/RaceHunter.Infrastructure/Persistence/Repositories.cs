using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Abstractions;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Projects;
using RaceHunter.Domain.Runs;
using RaceHunter.Domain.Tracing;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class ProjectRepository(RaceHunterDbContext context) : IProjectRepository
{
    public Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        context.Projects.Add(new ProjectRecord
        {
            Id = project.Id,
            Name = project.Name,
            CreatedAtUtc = project.CreatedAtUtc
        });
        return Task.CompletedTask;
    }

    public async Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await context.Projects.AsNoTracking().SingleOrDefaultAsync(project => project.Id == id, cancellationToken);
        return record is null ? null : Project.Rehydrate(record.Id, record.Name, record.CreatedAtUtc);
    }
}

internal sealed class RunStore(RaceHunterDbContext context) : IRunStore, ITraceStore, IRunAttemptStore
{
    public async Task AddAsync(ExperimentRun run, CancellationToken cancellationToken)
    {
        context.Runs.Add(ToRecord(run));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(ExperimentRun run, CancellationToken cancellationToken)
    {
        var record = context.Runs.Local.SingleOrDefault(item => item.Id == run.Id)
            ?? await context.Runs.SingleAsync(item => item.Id == run.Id, cancellationToken);
        Apply(record, run);
        var persistedCursors = await context.RunEvents
            .Where(item => item.RunId == run.Id)
            .Select(item => item.Cursor)
            .ToListAsync(cancellationToken);
        var trackedCursors = context.RunEvents.Local.Where(item => item.RunId == run.Id).Select(item => item.Cursor);
        var existing = persistedCursors.Concat(trackedCursors).ToHashSet();
        foreach (var item in run.Events.Where(item => !existing.Contains(item.Cursor)))
        {
            context.RunEvents.Add(new RunEventRecord
            {
                RunId = run.Id,
                Cursor = item.Cursor,
                Kind = item.Kind,
                Message = item.Message,
                OccurredAtUtc = item.OccurredAtUtc
            });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExperimentRun?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await context.Runs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null) return null;
        var events = await GetEventsAsync(id, 0, cancellationToken);
        return ExperimentRun.Rehydrate(
            record.Id,
            ToBudget(record),
            Enum.Parse<RunStatus>(record.Status),
            record.CreatedAtUtc,
            record.StartedAtUtc,
            record.CompletedAtUtc,
            record.CancellationRequestedAtUtc,
            events);
    }

    public async Task<IReadOnlyList<RunEvent>> GetEventsAsync(Guid id, long after, CancellationToken cancellationToken) =>
        await context.RunEvents.AsNoTracking()
            .Where(item => item.RunId == id && item.Cursor > after)
            .OrderBy(item => item.Cursor)
            .Take(500)
            .Select(item => new RunEvent(item.Cursor, item.Kind, item.Message, item.OccurredAtUtc))
            .ToArrayAsync(cancellationToken);

    public async Task<bool> RequestCancellationAsync(Guid id, DateTime requestedAtUtc, CancellationToken cancellationToken) =>
        await context.Runs
            .Where(item => item.Id == id &&
                item.CancellationRequestedAtUtc == null &&
                item.Status != nameof(RunStatus.Completed) &&
                item.Status != nameof(RunStatus.Failed) &&
                item.Status != nameof(RunStatus.Cancelled))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.CancellationRequestedAtUtc, requestedAtUtc), cancellationToken) > 0;

    public async Task AppendAsync(TraceEvent traceEvent, CancellationToken cancellationToken)
    {
        context.TraceEvents.Add(new TraceEventRecord
        {
            RunId = traceEvent.RunId,
            Sequence = traceEvent.Sequence,
            AttemptId = traceEvent.AttemptId,
            ActorId = traceEvent.ActorId,
            StepId = traceEvent.StepId,
            Kind = traceEvent.Kind,
            RequestId = traceEvent.RequestId,
            OccurredAtUtc = traceEvent.OccurredAtUtc
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(RunAttempt attempt, CancellationToken cancellationToken)
    {
        context.RunAttempts.Add(ToRecord(attempt));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(RunAttempt attempt, CancellationToken cancellationToken)
    {
        var record = context.RunAttempts.Local.SingleOrDefault(item => item.Id == attempt.Id)
            ?? await context.RunAttempts.SingleAsync(item => item.Id == attempt.Id, cancellationToken);
        record.Status = attempt.Status.ToString();
        record.CompletedAtUtc = attempt.CompletedAtUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TraceEvent>> GetAsync(Guid runId, long after, CancellationToken cancellationToken) =>
        await context.TraceEvents.AsNoTracking()
            .Where(item => item.RunId == runId && item.Sequence > after)
            .OrderBy(item => item.Sequence)
            .Take(500)
            .Select(item => new TraceEvent(item.Sequence, item.RunId, item.AttemptId, item.ActorId, item.StepId, item.Kind, item.RequestId, item.OccurredAtUtc))
            .ToArrayAsync(cancellationToken);

    private static RunRecord ToRecord(ExperimentRun run)
    {
        var record = new RunRecord { Id = run.Id, Status = run.Status.ToString() };
        Apply(record, run);
        return record;
    }

    private static void Apply(RunRecord record, ExperimentRun run)
    {
        record.Status = run.Status.ToString();
        record.MaxActors = run.Budget.MaxActors;
        record.MaxConcurrentActors = run.Budget.MaxConcurrentActors;
        record.MaxRequests = run.Budget.MaxRequests;
        record.MaxModelCalls = run.Budget.MaxModelCalls;
        record.MaxDurationMilliseconds = checked((long)run.Budget.MaxDuration.TotalMilliseconds);
        record.MaxRetries = run.Budget.MaxRetries;
        record.CreatedAtUtc = run.CreatedAtUtc;
        record.StartedAtUtc = run.StartedAtUtc;
        record.CompletedAtUtc = run.CompletedAtUtc;
        if (run.CancellationRequestedAtUtc.HasValue)
            record.CancellationRequestedAtUtc = run.CancellationRequestedAtUtc;
    }

    private static ExperimentBudget ToBudget(RunRecord record) => new(
        record.MaxActors,
        record.MaxConcurrentActors,
        record.MaxRequests,
        record.MaxModelCalls,
        TimeSpan.FromMilliseconds(record.MaxDurationMilliseconds),
        record.MaxRetries);

    private static RunAttemptRecord ToRecord(RunAttempt attempt) => new()
    {
        Id = attempt.Id,
        RunId = attempt.RunId,
        Strategy = attempt.Strategy,
        Seed = attempt.Seed,
        Status = attempt.Status.ToString(),
        StartedAtUtc = attempt.StartedAtUtc,
        CompletedAtUtc = attempt.CompletedAtUtc
    };
}
