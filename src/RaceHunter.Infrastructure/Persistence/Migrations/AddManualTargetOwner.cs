using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180010_AddManualTargetOwner")]
internal sealed class AddManualTargetOwner : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "owner_key_id",
        table: "target_systems",
        type: "character varying(64)",
        maxLength: 64,
        nullable: false,
        defaultValue: "");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "owner_key_id", table: "target_systems");
}
