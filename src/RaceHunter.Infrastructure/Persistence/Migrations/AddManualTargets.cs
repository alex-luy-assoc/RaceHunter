using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180006_AddManualTargets")]
internal sealed class AddManualTargets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "target_systems",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                credential_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                operation_paths_json = table.Column<string>(type: "jsonb", nullable: false),
                sensitive_json_paths_json = table.Column<string>(type: "jsonb", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_target_systems", value => value.id));
        migrationBuilder.CreateIndex("IX_target_systems_base_url", "target_systems", "base_url", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("target_systems");
}
