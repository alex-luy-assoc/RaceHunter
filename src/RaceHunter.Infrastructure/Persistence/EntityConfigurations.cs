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

internal sealed class HuntConfiguration : IEntityTypeConfiguration<HuntRecord>
{
    public void Configure(EntityTypeBuilder<HuntRecord> builder)
    {
        builder.ToTable("experiments");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.Objective).HasColumnName("objective").HasMaxLength(1000).IsRequired();
        builder.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(item => item.MaxActors).HasColumnName("max_actors");
        builder.Property(item => item.MaxConcurrentActors).HasColumnName("max_concurrent_actors");
        builder.Property(item => item.MaxRequests).HasColumnName("max_requests");
        builder.Property(item => item.MaxModelCalls).HasColumnName("max_model_calls");
        builder.Property(item => item.MaxDurationMilliseconds).HasColumnName("max_duration_ms");
        builder.Property(item => item.MaxRetries).HasColumnName("max_retries");
        builder.Property(item => item.PlanVersion).HasColumnName("plan_version").HasMaxLength(64);
        builder.Property(item => item.PlanJson).HasColumnName("plan_json").HasColumnType("jsonb");
        builder.Property(item => item.ApprovedPlanVersion).HasColumnName("approved_plan_version").HasMaxLength(64);
        builder.Property(item => item.ApprovalKey).HasColumnName("approval_key").HasMaxLength(160);
        builder.Property(item => item.RunId).HasColumnName("run_id");
        builder.Property(item => item.FailureOutcome).HasColumnName("failure_outcome").HasMaxLength(64);
        builder.Property(item => item.FailureDiagnostic).HasColumnName("failure_diagnostic").HasMaxLength(500);
        builder.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(item => item.ApprovalKey).IsUnique();
        builder.HasIndex(item => item.RunId).IsUnique();
    }
}

internal sealed class HuntEventConfiguration : IEntityTypeConfiguration<HuntEventRecord>
{
    public void Configure(EntityTypeBuilder<HuntEventRecord> builder)
    {
        builder.ToTable("hunt_events");
        builder.HasKey(item => new { item.HuntId, item.Cursor });
        builder.Property(item => item.HuntId).HasColumnName("hunt_id");
        builder.Property(item => item.Cursor).HasColumnName("cursor");
        builder.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Message).HasColumnName("message").HasMaxLength(1000).IsRequired();
        builder.Property(item => item.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamp with time zone");
        builder.HasOne(item => item.Hunt).WithMany(item => item.Events).HasForeignKey(item => item.HuntId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OutboxConfiguration : IEntityTypeConfiguration<OutboxRecord>
{
    public void Configure(EntityTypeBuilder<OutboxRecord> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.Version).HasColumnName("version").HasMaxLength(32).IsRequired();
        builder.Property(item => item.WorkId).HasColumnName("work_id");
        builder.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(64).IsRequired();
        builder.Property(item => item.SubjectId).HasColumnName("subject_id");
        builder.Property(item => item.CorrelationId).HasColumnName("correlation_id").HasMaxLength(160).IsRequired();
        builder.Property(item => item.WorkCreatedAtUtc).HasColumnName("work_created_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(item => item.PublishAttempts).HasColumnName("publish_attempts");
        builder.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(item => item.PublishedAtUtc).HasColumnName("published_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(item => item.WorkId).IsUnique();
        builder.HasIndex(item => item.PublishedAtUtc);
    }
}

internal sealed class WorkInboxConfiguration : IEntityTypeConfiguration<WorkInboxRecord>
{
    public void Configure(EntityTypeBuilder<WorkInboxRecord> builder)
    {
        builder.ToTable("work_inbox");
        builder.HasKey(item => item.WorkId);
        builder.Property(item => item.WorkId).HasColumnName("work_id").ValueGeneratedNever();
        builder.Property(item => item.MessageId).HasColumnName("message_id").HasMaxLength(200).IsRequired();
        builder.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(item => item.DeliveryAttempt).HasColumnName("delivery_attempt");
        builder.Property(item => item.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(160);
        builder.Property(item => item.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(item => item.CheckpointBoundary).HasColumnName("checkpoint_boundary").HasMaxLength(120);
        builder.Property(item => item.CheckpointIteration).HasColumnName("checkpoint_iteration");
        builder.Property(item => item.CheckpointStateJson).HasColumnName("checkpoint_state_json").HasColumnType("jsonb");
        builder.Property(item => item.CheckpointAtUtc).HasColumnName("checkpoint_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(item => item.FailureCategory).HasColumnName("failure_category").HasMaxLength(64);
        builder.Property(item => item.FailureDiagnostic).HasColumnName("failure_diagnostic").HasMaxLength(500);
        builder.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(item => item.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(item => item.MessageId).IsUnique();
        builder.HasIndex(item => item.LeaseExpiresAtUtc);
    }
}

internal sealed class DeadLetterConfiguration : IEntityTypeConfiguration<DeadLetterRecord>
{
    public void Configure(EntityTypeBuilder<DeadLetterRecord> builder)
    {
        builder.ToTable("dead_letters");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.WorkId).HasColumnName("work_id");
        builder.Property(item => item.Category).HasColumnName("category").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Diagnostic).HasColumnName("diagnostic").HasMaxLength(500).IsRequired();
        builder.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(item => item.WorkId).IsUnique();
    }
}

internal sealed class AgentIterationConfiguration : IEntityTypeConfiguration<AgentIterationPersistenceRecord>
{
    public void Configure(EntityTypeBuilder<AgentIterationPersistenceRecord> builder)
    {
        builder.ToTable("agent_iterations");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.RunId).HasColumnName("run_id");
        builder.Property(item => item.Iteration).HasColumnName("iteration");
        builder.Property(item => item.EvidenceSummary).HasColumnName("evidence_summary").HasMaxLength(1000).IsRequired();
        builder.Property(item => item.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(item => item.RationaleSummary).HasColumnName("rationale_summary").HasMaxLength(1000).IsRequired();
        builder.Property(item => item.ModelId).HasColumnName("model_id").HasMaxLength(120).IsRequired();
        builder.Property(item => item.SchemaVersion).HasColumnName("schema_version").HasMaxLength(64).IsRequired();
        builder.Property(item => item.ModelInvocationId).HasColumnName("model_invocation_id").HasMaxLength(160).IsRequired();
        builder.Property(item => item.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(item => new { item.RunId, item.Iteration }).IsUnique();
        builder.HasOne<RunRecord>().WithMany().HasForeignKey(item => item.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}
