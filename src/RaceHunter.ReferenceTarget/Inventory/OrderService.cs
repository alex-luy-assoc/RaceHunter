using Microsoft.EntityFrameworkCore;

namespace RaceHunter.ReferenceTarget.Inventory;

internal sealed class OrderService(InventoryDbContext database, ControlledCheckpoint checkpoint)
{
    public async Task<OrderOutcome> PlaceAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0 || string.IsNullOrWhiteSpace(request.ActorId))
            return OrderOutcome.Invalid();
        var key = NormalizeOptionalKey(request.IdempotencyKey);
        if (key is not null)
        {
            var existing = await database.Orders.AsNoTracking().SingleOrDefaultAsync(item => item.IdempotencyKey == key, cancellationToken);
            if (existing is not null)
            {
                await checkpoint.ReachAsync(request.Checkpoint, cancellationToken);
                return new OrderOutcome(existing.Status, existing.CorrelationId, existing.SuccessfulOrders, true);
            }
        }
        request = request with { ActorId = request.ActorId.Trim(), IdempotencyKey = key, ReplayScope = NormalizeOptionalKey(request.ReplayScope) };

        var state = await database.Inventory.AsNoTracking().SingleAsync(cancellationToken);
        return string.Equals(state.Mode, "fixed", StringComparison.Ordinal)
            ? await PlaceFixedAsync(request, cancellationToken)
            : await PlaceVulnerableAsync(state, request, cancellationToken);
    }

    private async Task<OrderOutcome> PlaceVulnerableAsync(InventoryState snapshot, OrderRequest request, CancellationToken cancellationToken)
    {
        if (snapshot.Available < request.Quantity) return await RecordAsync(request, "out-of-stock", 0, cancellationToken);
        await checkpoint.ReachAsync(request.Checkpoint, cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Inventory.ExecuteUpdateAsync(setters => setters
            .SetProperty(state => state.Available, state => state.Available - request.Quantity)
            .SetProperty(state => state.SuccessfulOrders, state => state.SuccessfulOrders + 1), cancellationToken);
        var outcome = await RecordAsync(request, "created", null, cancellationToken);
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
        if (updated == 0)
        {
            var rejected = await RecordAsync(request, "out-of-stock", 0, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return rejected;
        }
        var outcome = await RecordAsync(request, "created", null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private async Task<OrderOutcome> RecordAsync(OrderRequest request, string status, int? knownSuccessfulOrders, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var successfulOrders = knownSuccessfulOrders ?? await database.Inventory.AsNoTracking()
            .Select(state => state.SuccessfulOrders)
            .SingleAsync(cancellationToken);
        database.Orders.Add(new OrderRecord
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            ActorId = request.ActorId,
            IdempotencyKey = request.IdempotencyKey,
            Quantity = request.Quantity,
            Status = status,
            SuccessfulOrders = successfulOrders,
            CreatedAtUtc = DateTime.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
        return new OrderOutcome(status, correlationId, successfulOrders, false);
    }

    private static string? NormalizeOptionalKey(string? value)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 160) throw new ArgumentException("IdempotencyKey must contain between 1 and 160 characters.");
        return normalized;
    }

}

internal sealed record OrderRequest(string ActorId, int Quantity, string Checkpoint, string? IdempotencyKey = null, string? ReplayScope = null);
internal sealed record OrderOutcome(string Status, Guid? CorrelationId, int SuccessfulOrders, bool Replayed = false)
{
    internal static OrderOutcome Created(Guid id, int successfulOrders) => new("created", id, successfulOrders);
    internal static OrderOutcome OutOfStock() => new("out-of-stock", null, 0);
    internal static OrderOutcome Invalid() => new("invalid", null, 0);
}
