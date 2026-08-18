using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180008_AddOutboxTraceContext")]
internal sealed class AddOutboxTraceContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("trace_parent", "outbox_messages", "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("trace_state", "outbox_messages", "character varying(512)", maxLength: 512, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("trace_parent", "outbox_messages");
        migrationBuilder.DropColumn("trace_state", "outbox_messages");
    }
}
