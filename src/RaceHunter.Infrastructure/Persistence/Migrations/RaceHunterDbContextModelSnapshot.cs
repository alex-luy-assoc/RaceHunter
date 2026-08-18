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
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.SecurityAuditEventRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("Category").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)").HasColumnName("category");
            entity.Property<DateTime>("OccurredAtUtc").HasColumnType("timestamp with time zone").HasColumnName("occurred_at_utc");
            entity.Property<string>("Outcome").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("outcome");
            entity.Property<string>("SanitizedDetail").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("sanitized_detail");
            entity.Property<Guid?>("ScopeId").HasColumnType("uuid").HasColumnName("scope_id");
            entity.Property<string>("Stage").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("stage");
            entity.HasKey("Id");
            entity.HasIndex("OccurredAtUtc");
            entity.ToTable("security_audit_events");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.TargetSystemRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("BaseUrl").IsRequired().HasMaxLength(2048).HasColumnType("character varying(2048)").HasColumnName("base_url");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<string>("CredentialReference").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("credential_reference");
            entity.Property<string>("Host").IsRequired().HasMaxLength(253).HasColumnType("character varying(253)").HasColumnName("host");
            entity.Property<string>("OperationPathsJson").IsRequired().HasColumnType("jsonb").HasColumnName("operation_paths_json");
            entity.Property<string>("OwnerKeyId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("owner_key_id");
            entity.Property<string>("SensitiveJsonPathsJson").IsRequired().HasColumnType("jsonb").HasColumnName("sensitive_json_paths_json");
            entity.HasKey("Id");
            entity.HasIndex("BaseUrl").IsUnique();
            entity.ToTable("target_systems");
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
            entity.Property<Guid?>("ManualTargetId").HasColumnType("uuid").HasColumnName("manual_target_id");
            entity.Property<string>("Objective").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)").HasColumnName("objective");
            entity.Property<string>("PlanJson").HasColumnType("jsonb").HasColumnName("plan_json");
            entity.Property<string>("PlanVersion").HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("plan_version");
            entity.Property<Guid?>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("status");
            entity.HasKey("Id");
            entity.HasIndex("ApprovalKey").IsUnique();
            entity.HasIndex("RunId").IsUnique();
            entity.HasIndex("ManualTargetId");
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
            entity.Property<string>("TraceParent").HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("trace_parent");
            entity.Property<string>("TraceState").HasMaxLength(512).HasColumnType("character varying(512)").HasColumnName("trace_state");
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
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.FindingRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("AgentInterpretation").IsRequired().HasMaxLength(2000).HasColumnType("character varying(2000)").HasColumnName("agent_interpretation");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<string>("InvariantOutcome").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("invariant_outcome");
            entity.Property<string>("InvariantSummary").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)").HasColumnName("invariant_summary");
            entity.Property<string>("InvariantVersionId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("invariant_version_id");
            entity.Property<Guid>("ReplayArtifactId").HasColumnType("uuid").HasColumnName("replay_artifact_id");
            entity.Property<Guid>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<string>("TraceReferencesJson").IsRequired().HasColumnType("jsonb").HasColumnName("trace_references_json");
            entity.HasKey("Id");
            entity.HasIndex("ReplayArtifactId").IsUnique();
            entity.HasIndex("RunId").IsUnique();
            entity.ToTable("findings");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.FindingReproductionRecord", entity =>
        {
            entity.Property<Guid>("FindingId").HasColumnType("uuid").HasColumnName("finding_id");
            entity.Property<int>("Attempt").HasColumnType("integer").HasColumnName("attempt");
            entity.Property<string>("Outcome").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("outcome");
            entity.Property<string>("TraceReferencesJson").IsRequired().HasColumnType("jsonb").HasColumnName("trace_references_json");
            entity.HasKey("FindingId", "Attempt");
            entity.ToTable("finding_reproductions");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.FindingProbeCheckpointRecord", entity =>
        {
            entity.Property<Guid>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<string>("ProbeKey").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("probe_key");
            entity.Property<string>("CandidateJson").IsRequired().HasColumnType("jsonb").HasColumnName("candidate_json");
            entity.Property<DateTime>("CompletedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("completed_at_utc");
            entity.Property<string>("Outcome").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("outcome");
            entity.Property<int>("Ordinal").HasColumnType("integer").HasColumnName("ordinal");
            entity.Property<string>("Phase").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("phase");
            entity.Property<int>("RequestsConsumed").HasColumnType("integer").HasColumnName("requests_consumed");
            entity.Property<string>("TraceReferencesJson").IsRequired().HasColumnType("jsonb").HasColumnName("trace_references_json");
            entity.HasKey("RunId", "ProbeKey");
            entity.ToTable("finding_probe_checkpoints");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ManualSetupExecutionRecord", entity =>
        {
            entity.Property<Guid>("RunId").HasColumnType("uuid").HasColumnName("run_id");
            entity.Property<string>("ExecutionKey").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("execution_key");
            entity.Property<string>("OperationId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("operation_id");
            entity.Property<DateTime?>("CompletedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("completed_at_utc");
            entity.Property<string>("IdempotencyMode").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("idempotency_mode");
            entity.Property<int>("PhysicalRequestsReserved").HasColumnType("integer").HasColumnName("physical_requests_reserved");
            entity.Property<DateTime>("ReservedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("reserved_at_utc");
            entity.Property<string>("Status").IsRequired().HasMaxLength(24).HasColumnType("character varying(24)").HasColumnName("status");
            entity.Property<Guid>("TargetId").HasColumnType("uuid").HasColumnName("target_id");
            entity.HasKey("RunId", "ExecutionKey", "OperationId");
            entity.HasIndex("TargetId");
            entity.ToTable("manual_setup_executions");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ReplayArtifactRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<Guid>("FindingId").HasColumnType("uuid").HasColumnName("finding_id");
            entity.Property<string>("Fingerprint").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)").HasColumnName("fingerprint");
            entity.Property<string>("InvariantVersionId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("invariant_version_id");
            entity.Property<string>("RequestTemplateJson").IsRequired().HasColumnType("jsonb").HasColumnName("request_template_json");
            entity.Property<string>("ScenarioVersionId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("scenario_version_id");
            entity.Property<int>("Seed").HasColumnType("integer").HasColumnName("seed");
            entity.Property<string>("Strategy").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("strategy");
            entity.Property<string>("TargetSnapshot").IsRequired().HasColumnType("text").HasColumnName("target_snapshot");
            entity.HasKey("Id");
            entity.HasIndex("FindingId").IsUnique();
            entity.HasIndex("Fingerprint").IsUnique();
            entity.ToTable("replay_artifacts");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ReplayStepRecord", entity =>
        {
            entity.Property<Guid>("ArtifactId").HasColumnType("uuid").HasColumnName("artifact_id");
            entity.Property<int>("Position").HasColumnType("integer").HasColumnName("position");
            entity.Property<int>("ActorId").HasColumnType("integer").HasColumnName("actor_id");
            entity.Property<int>("OffsetMilliseconds").HasColumnType("integer").HasColumnName("offset_ms");
            entity.Property<string>("OperationId").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("operation_id");
            entity.Property<string>("StepId").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("step_id");
            entity.HasKey("ArtifactId", "Position");
            entity.ToTable("replay_steps");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ReplayAttemptRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<Guid>("ArtifactId").HasColumnType("uuid").HasColumnName("artifact_id");
            entity.Property<string>("ArtifactFingerprint").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)").HasColumnName("artifact_fingerprint");
            entity.Property<DateTime>("CompletedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("completed_at_utc");
            entity.Property<string>("IdempotencyKey").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("idempotency_key");
            entity.Property<string>("Outcome").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("outcome");
            entity.Property<string>("TargetMode").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("target_mode");
            entity.Property<string>("TraceReferencesJson").IsRequired().HasColumnType("jsonb").HasColumnName("trace_references_json");
            entity.HasKey("Id");
            entity.HasIndex("ArtifactId").IsUnique().HasFilter("target_mode = 'Fixed'");
            entity.HasIndex("ArtifactId", "IdempotencyKey").IsUnique();
            entity.ToTable("replay_attempts");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ReplayExecutionClaimRecord", entity =>
        {
            entity.Property<Guid>("ArtifactId").HasColumnType("uuid").HasColumnName("artifact_id");
            entity.Property<DateTime>("ClaimedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("claimed_at_utc");
            entity.Property<string>("Owner").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("owner");
            entity.HasKey("ArtifactId");
            entity.ToTable("replay_execution_claims");
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
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.FindingRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.RunRecord", null)
                .WithMany()
                .HasForeignKey("RunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.HasOne("RaceHunter.Infrastructure.Persistence.ReplayArtifactRecord", null)
                .WithOne()
                .HasForeignKey("RaceHunter.Infrastructure.Persistence.FindingRecord", "ReplayArtifactId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.FindingReproductionRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.FindingRecord", "Finding")
                .WithMany("Reproductions")
                .HasForeignKey("FindingId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("Finding");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.FindingProbeCheckpointRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.RunRecord", null)
                .WithMany()
                .HasForeignKey("RunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ManualSetupExecutionRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.RunRecord", null)
                .WithMany()
                .HasForeignKey("RunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.HasOne("RaceHunter.Infrastructure.Persistence.TargetSystemRecord", null)
                .WithMany()
                .HasForeignKey("TargetId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ReplayAttemptRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.ReplayArtifactRecord", "Artifact")
                .WithMany("Attempts")
                .HasForeignKey("ArtifactId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("Artifact");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ReplayExecutionClaimRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.ReplayArtifactRecord", "Artifact")
                .WithMany()
                .HasForeignKey("ArtifactId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("Artifact");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ReplayStepRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.ReplayArtifactRecord", "Artifact")
                .WithMany("Steps")
                .HasForeignKey("ArtifactId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("Artifact");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.HuntRecord", entity =>
        {
            entity.HasOne("RaceHunter.Infrastructure.Persistence.TargetSystemRecord", null)
                .WithMany()
                .HasForeignKey("ManualTargetId")
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation("Events");
        });
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.RunRecord", entity => entity.Navigation("Events"));
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.FindingRecord", entity => entity.Navigation("Reproductions"));
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ReplayArtifactRecord", entity =>
        {
            entity.Navigation("Attempts");
            entity.Navigation("Steps");
        });
    }
}
