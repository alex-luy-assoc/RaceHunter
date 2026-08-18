using Microsoft.EntityFrameworkCore;

namespace RaceHunter.ReferenceTarget.Inventory;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    internal DbSet<InventoryState> Inventory => Set<InventoryState>();
    internal DbSet<OrderRecord> Orders => Set<OrderRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryState>(entity =>
        {
            entity.ToTable("inventory_state");
            entity.HasKey(state => state.Id);
            entity.Property(state => state.Mode).HasMaxLength(16);
        });
        modelBuilder.Entity<OrderRecord>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.ActorId).HasMaxLength(100);
            entity.HasIndex(order => order.CorrelationId).IsUnique();
        });
    }
}

internal sealed class InventoryState
{
    public int Id { get; set; } = 1;
    public int InitialQuantity { get; set; }
    public int Available { get; set; }
    public int SuccessfulOrders { get; set; }
    public required string Mode { get; set; }
}

internal sealed class OrderRecord
{
    public Guid Id { get; set; }
    public Guid CorrelationId { get; set; }
    public required string ActorId { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
