using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.ReferenceTarget.Inventory;

[DbContext(typeof(InventoryDbContext))]
[Migration("202608180001_InitialInventory")]
internal sealed class InventoryMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("inventory_state", table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false),
            InitialQuantity = table.Column<int>(type: "integer", nullable: false),
            Available = table.Column<int>(type: "integer", nullable: false),
            SuccessfulOrders = table.Column<int>(type: "integer", nullable: false),
            Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_inventory_state", value => value.Id));
        migrationBuilder.CreateTable("orders", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
            ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            Quantity = table.Column<int>(type: "integer", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_orders", value => value.Id));
        migrationBuilder.CreateIndex("IX_orders_CorrelationId", "orders", "CorrelationId", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("orders");
        migrationBuilder.DropTable("inventory_state");
    }
}
