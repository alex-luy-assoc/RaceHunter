using Microsoft.EntityFrameworkCore;
using RaceHunter.ReferenceTarget.Inventory;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<InventoryDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("ReferenceTarget")
        ?? throw new InvalidOperationException("ConnectionStrings:ReferenceTarget is required.");
    options.UseNpgsql(connectionString);
});
builder.Services.AddSingleton<ControlledCheckpoint>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddHealthChecks();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await database.Database.MigrateAsync();
    if (!await database.Inventory.AnyAsync())
    {
        database.Inventory.Add(new InventoryState { InitialQuantity = 1, Available = 1, SuccessfulOrders = 0, Mode = "vulnerable" });
        await database.SaveChangesAsync();
    }
}

app.MapHealthChecks("/healthz", new() { ResponseWriter = async (context, _) => await context.Response.WriteAsync("Healthy") });
app.MapGet("/api/inventory", async (InventoryDbContext database, CancellationToken cancellationToken) =>
{
    var state = await database.Inventory.AsNoTracking().SingleAsync(cancellationToken);
    return Results.Ok(new { state.Available, state.SuccessfulOrders, state.Mode });
});
app.MapPost("/api/orders", async (OrderRequest request, OrderService service, CancellationToken cancellationToken) =>
{
    var result = await service.PlaceAsync(request, cancellationToken);
    return result.Status switch
    {
        "created" => Results.Created("/api/orders", new { result.CorrelationId, result.SuccessfulOrders, result.Replayed }),
        "out-of-stock" => Results.Conflict(new { error = "Insufficient inventory.", result.CorrelationId, result.SuccessfulOrders, result.Replayed }),
        _ => Results.BadRequest(new { error = "Actor ID and a positive quantity are required." })
    };
});
app.MapPost("/demo/reset", async (HttpRequest request, ResetRequest reset, InventoryDbContext database, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var expectedKey = configuration["DemoControl:Key"];
    if (string.IsNullOrEmpty(expectedKey) || request.Headers["X-Demo-Control-Key"] != expectedKey) return Results.Unauthorized();
    if (reset.Quantity < 0 || reset.Mode is not ("vulnerable" or "fixed")) return Results.BadRequest();
    var operationKey = request.Headers["X-RaceHunter-Idempotency-Key"].ToString().Trim();
    var replayScope = request.Headers["X-RaceHunter-Replay-Scope"].ToString().Trim();
    if (operationKey.Length > 160 || replayScope.Length > 160) return Results.BadRequest();
    if (operationKey.Length > 0)
    {
        var completed = await database.ResetOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdempotencyKey == operationKey, cancellationToken);
        if (completed is not null)
            return completed.Quantity == reset.Quantity && completed.Mode == reset.Mode ? Results.NoContent() : Results.Conflict();
    }
    await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
    await database.Inventory.ExecuteUpdateAsync(setters => setters
        .SetProperty(state => state.InitialQuantity, reset.Quantity)
        .SetProperty(state => state.Available, reset.Quantity)
        .SetProperty(state => state.SuccessfulOrders, 0)
        .SetProperty(state => state.Mode, reset.Mode), cancellationToken);
    if (operationKey.Length > 0)
    {
        database.ResetOperations.Add(new ResetOperationRecord
        {
            IdempotencyKey = operationKey,
            ReplayScope = replayScope.Length == 0 ? null : replayScope,
            Quantity = reset.Quantity,
            Mode = reset.Mode,
            CompletedAtUtc = DateTime.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
    }
    await transaction.CommitAsync(cancellationToken);
    return Results.NoContent();
});
app.MapPost("/demo/order-status", async (HttpRequest request, OrderStatusRequest status, InventoryDbContext database, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var expectedKey = configuration["DemoControl:Key"];
    if (string.IsNullOrEmpty(expectedKey) || request.Headers["X-Demo-Control-Key"] != expectedKey) return Results.Unauthorized();
    if (status.IdempotencyKeys.Count > 100 || status.IdempotencyKeys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 160))
        return Results.BadRequest();
    var completed = await database.Orders.AsNoTracking()
        .Where(item => item.IdempotencyKey != null && status.IdempotencyKeys.Contains(item.IdempotencyKey))
        .Select(item => new { IdempotencyKey = item.IdempotencyKey!, RequestId = item.CorrelationId.ToString("N") })
        .ToArrayAsync(cancellationToken);
    return Results.Ok(new { missing = status.IdempotencyKeys.Distinct(StringComparer.Ordinal).Count() - completed.Select(item => item.IdempotencyKey).Distinct(StringComparer.Ordinal).Count(), completed });
});
app.Run();

internal sealed record ResetRequest(int Quantity, string Mode);
internal sealed record OrderStatusRequest(IReadOnlyList<string> IdempotencyKeys);
public partial class Program;
