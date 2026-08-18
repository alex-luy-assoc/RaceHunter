using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180009_ExpandReplayTargetSnapshot")]
internal sealed class ExpandReplayTargetSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AlterColumn<string>(
        "target_snapshot", "replay_artifacts", "text", nullable: false,
        oldClrType: typeof(string), oldType: "character varying(500)", oldMaxLength: 500);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.AlterColumn<string>(
        "target_snapshot", "replay_artifacts", "character varying(500)", maxLength: 500, nullable: false,
        oldClrType: typeof(string), oldType: "text");
}
