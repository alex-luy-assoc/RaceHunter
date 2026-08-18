using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180005_AddFindingProbeCheckpoints")]
internal sealed class AddFindingProbeCheckpoints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "finding_probe_checkpoints",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                probe_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ordinal = table.Column<int>(type: "integer", nullable: false),
                candidate_json = table.Column<string>(type: "jsonb", nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                trace_references_json = table.Column<string>(type: "jsonb", nullable: false),
                requests_consumed = table.Column<int>(type: "integer", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finding_probe_checkpoints", value => new { value.run_id, value.probe_key });
                table.ForeignKey("FK_finding_probe_checkpoints_experiment_runs_run_id", value => value.run_id, "experiment_runs", "id", onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("finding_probe_checkpoints");
}
