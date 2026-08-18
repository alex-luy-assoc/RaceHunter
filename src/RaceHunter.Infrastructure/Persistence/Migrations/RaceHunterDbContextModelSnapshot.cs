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
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.RunRecord", entity => entity.Navigation("Events"));
    }
}
