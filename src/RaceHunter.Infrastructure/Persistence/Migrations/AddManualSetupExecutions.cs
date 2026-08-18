using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180011_AddManualSetupExecutions")]
internal sealed class AddManualSetupExecutions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "manual_setup_executions",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                execution_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                operation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                physical_requests_reserved = table.Column<int>(type: "integer", nullable: false),
                reserved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_manual_setup_executions", item => new { item.run_id, item.execution_key, item.operation_id });
                table.ForeignKey("fk_manual_setup_executions_runs", item => item.run_id, "experiment_runs", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("fk_manual_setup_executions_targets", item => item.target_id, "target_systems", "id", onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "manual_setup_executions");
}
