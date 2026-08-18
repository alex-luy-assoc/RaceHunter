using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Abstractions;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class RaceHunterDbContext(DbContextOptions<RaceHunterDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<ProjectRecord> Projects => Set<ProjectRecord>();
    internal DbSet<TargetSystemRecord> TargetSystems => Set<TargetSystemRecord>();
    internal DbSet<RunRecord> Runs => Set<RunRecord>();
    internal DbSet<RunEventRecord> RunEvents => Set<RunEventRecord>();
    internal DbSet<RunAttemptRecord> RunAttempts => Set<RunAttemptRecord>();
    internal DbSet<TraceEventRecord> TraceEvents => Set<TraceEventRecord>();
    internal DbSet<HuntRecord> Hunts => Set<HuntRecord>();
    internal DbSet<HuntEventRecord> HuntEvents => Set<HuntEventRecord>();
    internal DbSet<OutboxRecord> OutboxMessages => Set<OutboxRecord>();
    internal DbSet<WorkInboxRecord> WorkInbox => Set<WorkInboxRecord>();
    internal DbSet<DeadLetterRecord> DeadLetters => Set<DeadLetterRecord>();
    internal DbSet<AgentIterationPersistenceRecord> AgentIterations => Set<AgentIterationPersistenceRecord>();
    internal DbSet<FindingRecord> Findings => Set<FindingRecord>();
    internal DbSet<FindingReproductionRecord> FindingReproductions => Set<FindingReproductionRecord>();
    internal DbSet<ReplayArtifactRecord> ReplayArtifacts => Set<ReplayArtifactRecord>();
    internal DbSet<ReplayStepRecord> ReplaySteps => Set<ReplayStepRecord>();
    internal DbSet<ReplayAttemptRecord> ReplayAttempts => Set<ReplayAttemptRecord>();
    internal DbSet<ReplayExecutionClaimRecord> ReplayExecutionClaims => Set<ReplayExecutionClaimRecord>();
    internal DbSet<FindingProbeCheckpointRecord> FindingProbeCheckpoints => Set<FindingProbeCheckpointRecord>();
    internal DbSet<SecurityAuditEventRecord> SecurityAuditEvents => Set<SecurityAuditEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new TargetSystemConfiguration());
        modelBuilder.ApplyConfiguration(new RunConfiguration());
        modelBuilder.ApplyConfiguration(new RunEventConfiguration());
        modelBuilder.ApplyConfiguration(new RunAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new TraceEventConfiguration());
        modelBuilder.ApplyConfiguration(new HuntConfiguration());
        modelBuilder.ApplyConfiguration(new HuntEventConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        modelBuilder.ApplyConfiguration(new WorkInboxConfiguration());
        modelBuilder.ApplyConfiguration(new DeadLetterConfiguration());
        modelBuilder.ApplyConfiguration(new AgentIterationConfiguration());
        modelBuilder.ApplyConfiguration(new FindingConfiguration());
        modelBuilder.ApplyConfiguration(new FindingReproductionConfiguration());
        modelBuilder.ApplyConfiguration(new ReplayArtifactConfiguration());
        modelBuilder.ApplyConfiguration(new ReplayStepConfiguration());
        modelBuilder.ApplyConfiguration(new ReplayAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new ReplayExecutionClaimConfiguration());
        modelBuilder.ApplyConfiguration(new FindingProbeCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new SecurityAuditEventConfiguration());
    }
}

internal sealed class SecurityAuditEventRecord
{
    public Guid Id { get; set; }
    public Guid? ScopeId { get; set; }
    public required string Stage { get; set; }
    public required string Category { get; set; }
    public required string Outcome { get; set; }
    public required string SanitizedDetail { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

internal sealed class ProjectRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class TargetSystemRecord
{
    public Guid Id { get; set; }
    public required string BaseUrl { get; set; }
    public required string Host { get; set; }
    public required string CredentialReference { get; set; }
    public required string OperationPathsJson { get; set; }
    public required string SensitiveJsonPathsJson { get; set; }
    public required string OwnerKeyId { get; set; }
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

internal sealed class HuntRecord
{
    public Guid Id { get; set; }
    public required string Objective { get; set; }
    public required string Status { get; set; }
    public int MaxActors { get; set; }
    public int MaxConcurrentActors { get; set; }
    public int MaxRequests { get; set; }
    public int MaxModelCalls { get; set; }
    public long MaxDurationMilliseconds { get; set; }
    public int MaxRetries { get; set; }
    public string? PlanVersion { get; set; }
    public string? PlanJson { get; set; }
    public string? ApprovedPlanVersion { get; set; }
    public string? ApprovalKey { get; set; }
    public Guid? RunId { get; set; }
    public Guid? ManualTargetId { get; set; }
    public string? FailureOutcome { get; set; }
    public string? FailureDiagnostic { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<HuntEventRecord> Events { get; set; } = [];
}

internal sealed class HuntEventRecord
{
    public Guid HuntId { get; set; }
    public long Cursor { get; set; }
    public required string Kind { get; set; }
    public required string Message { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public HuntRecord Hunt { get; set; } = null!;
}

internal sealed class OutboxRecord
{
    public Guid Id { get; set; }
    public required string Version { get; set; }
    public Guid WorkId { get; set; }
    public required string Kind { get; set; }
    public Guid SubjectId { get; set; }
    public required string CorrelationId { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public DateTime WorkCreatedAtUtc { get; set; }
    public int PublishAttempts { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

internal sealed class WorkInboxRecord
{
    public Guid WorkId { get; set; }
    public required string MessageId { get; set; }
    public required string Status { get; set; }
    public int DeliveryAttempt { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? CheckpointBoundary { get; set; }
    public int? CheckpointIteration { get; set; }
    public string? CheckpointStateJson { get; set; }
    public DateTime? CheckpointAtUtc { get; set; }
    public string? FailureCategory { get; set; }
    public string? FailureDiagnostic { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class DeadLetterRecord
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public required string Category { get; set; }
    public required string Diagnostic { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class AgentIterationPersistenceRecord
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int Iteration { get; set; }
    public required string EvidenceSummary { get; set; }
    public required string Action { get; set; }
    public required string RationaleSummary { get; set; }
    public required string ModelId { get; set; }
    public required string SchemaVersion { get; set; }
    public required string ModelInvocationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

internal sealed class FindingRecord
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public required string InvariantVersionId { get; set; }
    public required string InvariantOutcome { get; set; }
    public required string InvariantSummary { get; set; }
    public required string TraceReferencesJson { get; set; }
    public Guid ReplayArtifactId { get; set; }
    public required string AgentInterpretation { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<FindingReproductionRecord> Reproductions { get; set; } = [];
}

internal sealed class FindingReproductionRecord
{
    public Guid FindingId { get; set; }
    public int Attempt { get; set; }
    public required string Outcome { get; set; }
    public required string TraceReferencesJson { get; set; }
    public FindingRecord Finding { get; set; } = null!;
}

internal sealed class ReplayArtifactRecord
{
    public Guid Id { get; set; }
    public Guid FindingId { get; set; }
    public required string ScenarioVersionId { get; set; }
    public required string InvariantVersionId { get; set; }
    public required string TargetSnapshot { get; set; }
    public required string Strategy { get; set; }
    public int Seed { get; set; }
    public required string RequestTemplateJson { get; set; }
    public required string Fingerprint { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<ReplayStepRecord> Steps { get; set; } = [];
    public List<ReplayAttemptRecord> Attempts { get; set; } = [];
}

internal sealed class ReplayStepRecord
{
    public Guid ArtifactId { get; set; }
    public int Position { get; set; }
    public int ActorId { get; set; }
    public required string StepId { get; set; }
    public required string OperationId { get; set; }
    public int OffsetMilliseconds { get; set; }
    public ReplayArtifactRecord Artifact { get; set; } = null!;
}

internal sealed class ReplayAttemptRecord
{
    public Guid Id { get; set; }
    public Guid ArtifactId { get; set; }
    public required string TargetMode { get; set; }
    public required string Outcome { get; set; }
    public required string TraceReferencesJson { get; set; }
    public required string ArtifactFingerprint { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public ReplayArtifactRecord Artifact { get; set; } = null!;
}

internal sealed class ReplayExecutionClaimRecord
{
    public Guid ArtifactId { get; set; }
    public required string Owner { get; set; }
    public DateTime ClaimedAtUtc { get; set; }
    public ReplayArtifactRecord Artifact { get; set; } = null!;
}

internal sealed class FindingProbeCheckpointRecord
{
    public Guid RunId { get; set; }
    public required string ProbeKey { get; set; }
    public required string Phase { get; set; }
    public int Ordinal { get; set; }
    public required string CandidateJson { get; set; }
    public required string Outcome { get; set; }
    public required string TraceReferencesJson { get; set; }
    public int RequestsConsumed { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}
