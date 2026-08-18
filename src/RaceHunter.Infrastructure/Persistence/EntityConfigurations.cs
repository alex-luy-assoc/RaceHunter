using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<ProjectRecord>
{
    public void Configure(EntityTypeBuilder<ProjectRecord> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(project => project.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(project => project.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(project => project.Name).IsUnique();
    }
}

internal sealed class RunConfiguration : IEntityTypeConfiguration<RunRecord>
{
    public void Configure(EntityTypeBuilder<RunRecord> builder)
    {
        builder.ToTable("experiment_runs");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(run => run.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(run => run.MaxActors).HasColumnName("max_actors");
        builder.Property(run => run.MaxConcurrentActors).HasColumnName("max_concurrent_actors");
        builder.Property(run => run.MaxRequests).HasColumnName("max_requests");
        builder.Property(run => run.MaxModelCalls).HasColumnName("max_model_calls");
        builder.Property(run => run.MaxDurationMilliseconds).HasColumnName("max_duration_ms");
        builder.Property(run => run.MaxRetries).HasColumnName("max_retries");
        builder.Property(run => run.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(run => run.StartedAtUtc).HasColumnName("started_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(run => run.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(run => run.CancellationRequestedAtUtc).HasColumnName("cancellation_requested_at_utc").HasColumnType("timestamp with time zone");
    }
}

internal sealed class RunEventConfiguration : IEntityTypeConfiguration<RunEventRecord>
{
    public void Configure(EntityTypeBuilder<RunEventRecord> builder)
    {
        builder.ToTable("run_events");
        builder.HasKey(item => new { item.RunId, item.Cursor });
        builder.Property(item => item.RunId).HasColumnName("run_id");
        builder.Property(item => item.Cursor).HasColumnName("cursor");
        builder.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Message).HasColumnName("message").HasMaxLength(1000).IsRequired();
        builder.Property(item => item.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamp with time zone");
        builder.HasOne(item => item.Run).WithMany(run => run.Events).HasForeignKey(item => item.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RunAttemptConfiguration : IEntityTypeConfiguration<RunAttemptRecord>
{
    public void Configure(EntityTypeBuilder<RunAttemptRecord> builder)
    {
        builder.ToTable("run_attempts");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.RunId).HasColumnName("run_id");
        builder.Property(item => item.Strategy).HasColumnName("strategy").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Seed).HasColumnName("seed");
        builder.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(item => item.StartedAtUtc).HasColumnName("started_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(item => item.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamp with time zone");
        builder.HasOne<RunRecord>().WithMany().HasForeignKey(item => item.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => item.RunId);
    }
}

internal sealed class TraceEventConfiguration : IEntityTypeConfiguration<TraceEventRecord>
{
    public void Configure(EntityTypeBuilder<TraceEventRecord> builder)
    {
        builder.ToTable("trace_events");
        builder.HasKey(item => new { item.RunId, item.Sequence });
        builder.Property(item => item.RunId).HasColumnName("run_id");
        builder.Property(item => item.Sequence).HasColumnName("sequence");
        builder.Property(item => item.AttemptId).HasColumnName("attempt_id");
        builder.Property(item => item.ActorId).HasColumnName("actor_id");
        builder.Property(item => item.StepId).HasColumnName("step_id").HasMaxLength(120).IsRequired();
        builder.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(64).IsRequired();
        builder.Property(item => item.RequestId).HasColumnName("request_id").HasMaxLength(160).IsRequired();
        builder.Property(item => item.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamp with time zone");
        builder.HasOne<RunRecord>().WithMany().HasForeignKey(item => item.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RunAttemptRecord>().WithMany().HasForeignKey(item => item.AttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => item.AttemptId);
    }
}
