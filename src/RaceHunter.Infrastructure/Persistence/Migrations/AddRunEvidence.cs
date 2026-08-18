using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180002_AddRunEvidence")]
internal sealed class AddRunEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "experiment_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                max_actors = table.Column<int>(type: "integer", nullable: false),
                max_concurrent_actors = table.Column<int>(type: "integer", nullable: false),
                max_requests = table.Column<int>(type: "integer", nullable: false),
                max_model_calls = table.Column<int>(type: "integer", nullable: false),
                max_duration_ms = table.Column<long>(type: "bigint", nullable: false),
                max_retries = table.Column<int>(type: "integer", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                cancellation_requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_experiment_runs", value => value.id));

        migrationBuilder.CreateTable(
            name: "run_events",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                cursor = table.Column<long>(type: "bigint", nullable: false),
                kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_run_events", value => new { value.run_id, value.cursor });
                table.ForeignKey("FK_run_events_experiment_runs_run_id", value => value.run_id, "experiment_runs", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "run_attempts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                strategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                seed = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_run_attempts", value => value.id);
                table.ForeignKey("FK_run_attempts_experiment_runs_run_id", value => value.run_id, "experiment_runs", "id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_run_attempts_run_id", "run_attempts", "run_id");

        migrationBuilder.CreateTable(
            name: "trace_events",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false),
                attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<int>(type: "integer", nullable: false),
                step_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                request_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_trace_events", value => new { value.run_id, value.sequence });
                table.ForeignKey("FK_trace_events_experiment_runs_run_id", value => value.run_id, "experiment_runs", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_trace_events_run_attempts_attempt_id", value => value.attempt_id, "run_attempts", "id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_trace_events_attempt_id", "trace_events", "attempt_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("run_events");
        migrationBuilder.DropTable("trace_events");
        migrationBuilder.DropTable("run_attempts");
        migrationBuilder.DropTable("experiment_runs");
    }
}
