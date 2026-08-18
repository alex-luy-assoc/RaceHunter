using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180011_AddSecurityAuditEvents")]
internal sealed class AddSecurityAuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "security_audit_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                sanitized_detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_security_audit_events", item => item.id));
        migrationBuilder.CreateIndex(name: "ix_security_audit_events_occurred_at_utc", table: "security_audit_events", column: "occurred_at_utc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "security_audit_events");
}
