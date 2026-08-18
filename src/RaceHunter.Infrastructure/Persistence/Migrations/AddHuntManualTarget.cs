using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180007_AddHuntManualTarget")]
internal sealed class AddHuntManualTarget : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "manual_target_id",
            table: "experiments",
            type: "uuid",
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_experiments_manual_target_id",
            table: "experiments",
            column: "manual_target_id");
        migrationBuilder.AddForeignKey(
            name: "FK_experiments_target_systems_manual_target_id",
            table: "experiments",
            column: "manual_target_id",
            principalTable: "target_systems",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_experiments_target_systems_manual_target_id", "experiments");
        migrationBuilder.DropIndex("IX_experiments_manual_target_id", "experiments");
        migrationBuilder.DropColumn("manual_target_id", "experiments");
    }
}
