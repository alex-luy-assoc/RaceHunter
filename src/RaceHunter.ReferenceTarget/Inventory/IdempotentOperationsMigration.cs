using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.ReferenceTarget.Inventory;

[DbContext(typeof(InventoryDbContext))]
[Migration("202608180002_IdempotentOperations")]
internal sealed class IdempotentOperationsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("IdempotencyKey", "orders", type: "character varying(160)", maxLength: 160, nullable: true);
        migrationBuilder.AddColumn<string>("Status", "orders", type: "text", nullable: false, defaultValue: "created");
        migrationBuilder.AddColumn<int>("SuccessfulOrders", "orders", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.CreateIndex("IX_orders_IdempotencyKey", "orders", "IdempotencyKey", unique: true, filter: "\"IdempotencyKey\" IS NOT NULL");
        migrationBuilder.CreateTable("reset_operations", table => new
        {
            IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
            ReplayScope = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
            Quantity = table.Column<int>(type: "integer", nullable: false),
            Mode = table.Column<string>(type: "text", nullable: false),
            CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_reset_operations", value => value.IdempotencyKey));
        migrationBuilder.CreateIndex("IX_reset_operations_ReplayScope", "reset_operations", "ReplayScope", unique: true, filter: "\"ReplayScope\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("reset_operations");
        migrationBuilder.DropIndex("IX_orders_IdempotencyKey", "orders");
        migrationBuilder.DropColumn("IdempotencyKey", "orders");
        migrationBuilder.DropColumn("Status", "orders");
        migrationBuilder.DropColumn("SuccessfulOrders", "orders");
    }
}
