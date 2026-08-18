using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
internal sealed class RaceHunterDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.4");
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ProjectRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("name");
            entity.HasKey("Id");
            entity.HasIndex("Name").IsUnique();
            entity.ToTable("projects");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.RunRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<DateTime?>("CancellationRequestedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("cancellation_requested_at_utc");
            entity.Property<DateTime?>("CompletedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("completed_at_utc");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<int>("MaxActors").HasColumnType("integer").HasColumnName("max_actors");
            entity.Property<int>("MaxConcurrentActors").HasColumnType("integer").HasColumnName("max_concurrent_actors");
            entity.Property<long>("MaxDurationMilliseconds").HasColumnType("bigint").HasColumnName("max_duration_ms");
            entity.Property<int>("MaxModelCalls").HasColumnType("integer").HasColumnName("max_model_calls");
            entity.Property<int>("MaxRequests").HasColumnType("integer").HasColumnName("max_requests");
            entity.Property<int>("MaxRetries").HasColumnType("integer").HasColumnName("max_retries");
            entity.Property<DateTime?>("StartedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("started_at_utc");
            entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("status");
            entity.HasKey("Id");
            entity.ToTable("experiment_runs");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.RunEventRecord", entity =>
        {
            entity.Property<Guid>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<long>("Cursor").HasColumnType("bigint").HasColumnName("cursor");
            entity.Property<string>("Kind").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("kind");
            entity.Property<string>("Message").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)").HasColumnName("message");
            entity.Property<DateTime>("OccurredAtUtc").HasColumnType("timestamp with time zone").HasColumnName("occurred_at_utc");
            entity.HasKey("RunId", "Cursor");
            entity.ToTable("run_events");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.RunAttemptRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<DateTime?>("CompletedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("completed_at_utc");
            entity.Property<Guid>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<int>("Seed").HasColumnType("integer").HasColumnName("seed");
            entity.Property<DateTime>("StartedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("started_at_utc");
            entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("status");
            entity.Property<string>("Strategy").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("strategy");
            entity.HasKey("Id");
            entity.HasIndex("RunId");
            entity.ToTable("run_attempts");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.TraceEventRecord", entity =>
        {
            entity.Property<Guid>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<long>("Sequence").HasColumnType("bigint").HasColumnName("sequence");
            entity.Property<int>("ActorId").HasColumnType("integer").HasColumnName("actor_id");
            entity.Property<Guid>("AttemptId").HasColumnType("uuid").HasColumnName("attempt_id");
            entity.Property<string>("Kind").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("kind");
            entity.Property<DateTime>("OccurredAtUtc").HasColumnType("timestamp with time zone").HasColumnName("occurred_at_utc");
            entity.Property<string>("RequestId").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("request_id");
            entity.Property<string>("StepId").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("step_id");
            entity.HasKey("RunId", "Sequence");
            entity.HasIndex("AttemptId");
            entity.ToTable("trace_events");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.HuntRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("ApprovalKey").HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("approval_key");
            entity.Property<string>("ApprovedPlanVersion").HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("approved_plan_version");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<string>("FailureDiagnostic").HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("failure_diagnostic");
            entity.Property<string>("FailureOutcome").HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("failure_outcome");
            entity.Property<int>("MaxActors").HasColumnType("integer").HasColumnName("max_actors");
            entity.Property<int>("MaxConcurrentActors").HasColumnType("integer").HasColumnName("max_concurrent_actors");
            entity.Property<long>("MaxDurationMilliseconds").HasColumnType("bigint").HasColumnName("max_duration_ms");
            entity.Property<int>("MaxModelCalls").HasColumnType("integer").HasColumnName("max_model_calls");
            entity.Property<int>("MaxRequests").HasColumnType("integer").HasColumnName("max_requests");
            entity.Property<int>("MaxRetries").HasColumnType("integer").HasColumnName("max_retries");
            entity.Property<string>("Objective").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)").HasColumnName("objective");
            entity.Property<string>("PlanJson").HasColumnType("jsonb").HasColumnName("plan_json");
            entity.Property<string>("PlanVersion").HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("plan_version");
            entity.Property<Guid?>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("status");
            entity.HasKey("Id");
            entity.HasIndex("ApprovalKey").IsUnique();
            entity.HasIndex("RunId").IsUnique();
            entity.ToTable("experiments");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.HuntEventRecord", entity =>
        {
            entity.Property<Guid>("HuntId").HasColumnType("uuid").HasColumnName("hunt_id");
            entity.Property<long>("Cursor").HasColumnType("bigint").HasColumnName("cursor");
            entity.Property<string>("Kind").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("kind");
            entity.Property<string>("Message").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)").HasColumnName("message");
            entity.Property<DateTime>("OccurredAtUtc").HasColumnType("timestamp with time zone").HasColumnName("occurred_at_utc");
            entity.HasKey("HuntId", "Cursor");
            entity.ToTable("hunt_events");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.OutboxRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("CorrelationId").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("correlation_id");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<string>("Kind").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("kind");
            entity.Property<DateTime?>("PublishedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("published_at_utc");
            entity.Property<int>("PublishAttempts").HasColumnType("integer").HasColumnName("publish_attempts");
            entity.Property<Guid>("SubjectId").HasColumnType("uuid").HasColumnName("subject_id");
            entity.Property<string>("Version").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("version");
            entity.Property<DateTime>("WorkCreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("work_created_at_utc");
            entity.Property<Guid>("WorkId").HasColumnType("uuid").HasColumnName("work_id");
            entity.HasKey("Id");
            entity.HasIndex("PublishedAtUtc");
            entity.HasIndex("WorkId").IsUnique();
            entity.ToTable("outbox_messages");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.WorkInboxRecord", entity =>
        {
            entity.Property<Guid>("WorkId").HasColumnType("uuid").HasColumnName("work_id");
            entity.Property<DateTime?>("CheckpointAtUtc").HasColumnType("timestamp with time zone").HasColumnName("checkpoint_at_utc");
            entity.Property<string>("CheckpointBoundary").HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("checkpoint_boundary");
            entity.Property<int?>("CheckpointIteration").HasColumnType("integer").HasColumnName("checkpoint_iteration");
            entity.Property<string>("CheckpointStateJson").HasColumnType("jsonb").HasColumnName("checkpoint_state_json");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<int>("DeliveryAttempt").HasColumnType("integer").HasColumnName("delivery_attempt");
            entity.Property<string>("FailureCategory").HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("failure_category");
            entity.Property<string>("FailureDiagnostic").HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("failure_diagnostic");
            entity.Property<DateTime?>("LeaseExpiresAtUtc").HasColumnType("timestamp with time zone").HasColumnName("lease_expires_at_utc");
            entity.Property<string>("LeaseOwner").HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("lease_owner");
            entity.Property<string>("MessageId").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("message_id");
            entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("status");
            entity.Property<DateTime>("UpdatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("updated_at_utc");
            entity.HasKey("WorkId");
            entity.HasIndex("LeaseExpiresAtUtc");
            entity.HasIndex("MessageId").IsUnique();
            entity.ToTable("work_inbox");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.DeadLetterRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("Category").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("category");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<string>("Diagnostic").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("diagnostic");
            entity.Property<Guid>("WorkId").HasColumnType("uuid").HasColumnName("work_id");
            entity.HasKey("Id");
            entity.HasIndex("WorkId").IsUnique();
            entity.ToTable("dead_letters");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.AgentIterationPersistenceRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("Action").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("action");
            entity.Property<string>("EvidenceSummary").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)").HasColumnName("evidence_summary");
            entity.Property<int>("Iteration").HasColumnType("integer").HasColumnName("iteration");
            entity.Property<string>("ModelId").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("model_id");
            entity.Property<string>("ModelInvocationId").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("model_invocation_id");
            entity.Property<DateTime>("OccurredAtUtc").HasColumnType("timestamp with time zone").HasColumnName("occurred_at_utc");
            entity.Property<string>("RationaleSummary").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)").HasColumnName("rationale_summary");
            entity.Property<Guid>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<string>("SchemaVersion").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("schema_version");
            entity.HasKey("Id");
            entity.HasIndex("RunId", "Iteration").IsUnique();
            entity.ToTable("agent_iterations");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.HuntEventRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.HuntRecord", "Hunt")
                .WithMany("Events")
                .HasForeignKey("HuntId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("Hunt");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.RunEventRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.RunRecord", "Run")
                .WithMany("Events")
                .HasForeignKey("RunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("Run");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.RunAttemptRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.RunRecord", null)
                .WithMany()
                .HasForeignKey("RunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.TraceEventRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.RunAttemptRecord", null)
                .WithMany()
                .HasForeignKey("AttemptId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.HasOne("RaceHunter.Infrastructure.Persistence.RunRecord", null)
                .WithMany()
                .HasForeignKey("RunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.AgentIterationPersistenceRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.RunRecord", null)
                .WithMany()
                .HasForeignKey("RunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.HuntRecord", entity => entity.Navigation("Events"));
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.RunRecord", entity => entity.Navigation("Events"));
    }
}
