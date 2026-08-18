using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180004_AddFindingReplay")]
internal sealed class AddFindingReplay : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "replay_artifacts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                scenario_version_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                invariant_version_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_snapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                strategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                seed = table.Column<int>(type: "integer", nullable: false),
                request_template_json = table.Column<string>(type: "jsonb", nullable: false),
                fingerprint = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_replay_artifacts", value => value.id));

        migrationBuilder.CreateTable(
            name: "findings",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                invariant_version_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                invariant_outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                invariant_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                trace_references_json = table.Column<string>(type: "jsonb", nullable: false),
                replay_artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                agent_interpretation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_findings", value => value.id);
                table.ForeignKey("FK_findings_experiment_runs_run_id", value => value.run_id, "experiment_runs", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_findings_replay_artifacts_replay_artifact_id", value => value.replay_artifact_id, "replay_artifacts", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "replay_execution_claims",
            columns: table => new
            {
                artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                claimed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_replay_execution_claims", value => value.artifact_id);
                table.ForeignKey("FK_replay_execution_claims_replay_artifacts_artifact_id", value => value.artifact_id, "replay_artifacts", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "replay_steps",
            columns: table => new
            {
                artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                actor_id = table.Column<int>(type: "integer", nullable: false),
                step_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                operation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                offset_ms = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_replay_steps", value => new { value.artifact_id, value.position });
                table.ForeignKey("FK_replay_steps_replay_artifacts_artifact_id", value => value.artifact_id, "replay_artifacts", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "finding_reproductions",
            columns: table => new
            {
                finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                attempt = table.Column<int>(type: "integer", nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                trace_references_json = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finding_reproductions", value => new { value.finding_id, value.attempt });
                table.ForeignKey("FK_finding_reproductions_findings_finding_id", value => value.finding_id, "findings", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "replay_attempts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                trace_references_json = table.Column<string>(type: "jsonb", nullable: false),
                artifact_fingerprint = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_replay_attempts", value => value.id);
                table.ForeignKey("FK_replay_attempts_replay_artifacts_artifact_id", value => value.artifact_id, "replay_artifacts", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_replay_artifacts_finding_id", "replay_artifacts", "finding_id", unique: true);
        migrationBuilder.CreateIndex("IX_replay_artifacts_fingerprint", "replay_artifacts", "fingerprint", unique: true);
        migrationBuilder.CreateIndex("IX_findings_run_id", "findings", "run_id", unique: true);
        migrationBuilder.CreateIndex("IX_findings_replay_artifact_id", "findings", "replay_artifact_id", unique: true);
        migrationBuilder.CreateIndex("IX_replay_attempts_artifact_id_idempotency_key", "replay_attempts", new[] { "artifact_id", "idempotency_key" }, unique: true);
        migrationBuilder.CreateIndex("IX_replay_attempts_artifact_id_fixed", "replay_attempts", "artifact_id", unique: true, filter: "target_mode = 'Fixed'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("finding_reproductions");
        migrationBuilder.DropTable("replay_attempts");
        migrationBuilder.DropTable("replay_execution_claims");
        migrationBuilder.DropTable("findings");
        migrationBuilder.DropTable("replay_steps");
        migrationBuilder.DropTable("replay_artifacts");
    }
}
