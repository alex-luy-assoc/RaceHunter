using Microsoft.EntityFrameworkCore;

namespace RaceHunter.ReferenceTarget.Inventory;

internal sealed class OrderService(InventoryDbContext database, ControlledCheckpoint checkpoint)
{
    public async Task<OrderOutcome> PlaceAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0 || string.IsNullOrWhiteSpace(request.ActorId))
            return OrderOutcome.Invalid();

        var state = await database.Inventory.AsNoTracking().SingleAsync(cancellationToken);
        return string.Equals(state.Mode, "fixed", StringComparison.Ordinal)
            ? await PlaceFixedAsync(request, cancellationToken)
            : await PlaceVulnerableAsync(state, request, cancellationToken);
    }

    private async Task<OrderOutcome> PlaceVulnerableAsync(InventoryState snapshot, OrderRequest request, CancellationToken cancellationToken)
    {
        if (snapshot.Available < request.Quantity) return OrderOutcome.OutOfStock();
        await checkpoint.ReachAsync(request.Checkpoint, cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Inventory.ExecuteUpdateAsync(setters => setters
            .SetProperty(state => state.Available, state => state.Available - request.Quantity)
            .SetProperty(state => state.SuccessfulOrders, state => state.SuccessfulOrders + 1), cancellationToken);
        var outcome = await RecordAsync(request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private async Task<OrderOutcome> PlaceFixedAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var updated = await database.Inventory
            .Where(state => state.Available >= request.Quantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(state => state.Available, state => state.Available - request.Quantity)
                .SetProperty(state => state.SuccessfulOrders, state => state.SuccessfulOrders + 1), cancellationToken);
        if (updated == 0) return OrderOutcome.OutOfStock();
        var outcome = await RecordAsync(request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private async Task<OrderOutcome> RecordAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        database.Orders.Add(new OrderRecord
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            ActorId = request.ActorId,
            Quantity = request.Quantity,
            CreatedAtUtc = DateTime.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
        return OrderOutcome.Created(correlationId);
    }
}

internal sealed record OrderRequest(string ActorId, int Quantity, string Checkpoint);
internal sealed record OrderOutcome(string Status, Guid? CorrelationId)
{
    internal static OrderOutcome Created(Guid id) => new("created", id);
    internal static OrderOutcome OutOfStock() => new("out-of-stock", null);
    internal static OrderOutcome Invalid() => new("invalid", null);
}
