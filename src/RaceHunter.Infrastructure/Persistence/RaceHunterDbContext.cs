using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Abstractions;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class RaceHunterDbContext(DbContextOptions<RaceHunterDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<ProjectRecord> Projects => Set<ProjectRecord>();
    internal DbSet<RunRecord> Runs => Set<RunRecord>();
    internal DbSet<RunEventRecord> RunEvents => Set<RunEventRecord>();
    internal DbSet<RunAttemptRecord> RunAttempts => Set<RunAttemptRecord>();
    internal DbSet<TraceEventRecord> TraceEvents => Set<TraceEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new RunConfiguration());
        modelBuilder.ApplyConfiguration(new RunEventConfiguration());
        modelBuilder.ApplyConfiguration(new RunAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new TraceEventConfiguration());
    }
}

internal sealed class ProjectRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class RunRecord
{
    public Guid Id { get; set; }
    public required string Status { get; set; }
    public int MaxActors { get; set; }
    public int MaxConcurrentActors { get; set; }
    public int MaxRequests { get; set; }
    public int MaxModelCalls { get; set; }
    public long MaxDurationMilliseconds { get; set; }
    public int MaxRetries { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancellationRequestedAtUtc { get; set; }
    public List<RunEventRecord> Events { get; set; } = [];
}

internal sealed class RunEventRecord
{
    public Guid RunId { get; set; }
    public long Cursor { get; set; }
    public required string Kind { get; set; }
    public required string Message { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public RunRecord Run { get; set; } = null!;
}

internal sealed class RunAttemptRecord
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public required string Strategy { get; set; }
    public int Seed { get; set; }
    public required string Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

internal sealed class TraceEventRecord
{
    public Guid RunId { get; set; }
    public long Sequence { get; set; }
    public Guid AttemptId { get; set; }
    public int ActorId { get; set; }
    public required string StepId { get; set; }
    public required string Kind { get; set; }
    public required string RequestId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
