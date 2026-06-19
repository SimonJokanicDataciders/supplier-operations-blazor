using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Services;

public partial class InventoryService
{
    public async Task<List<StockMovement>> GetStockMovementsAsync(
        int? productId = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.StockMovements
            .Include(movement => movement.Product)
            .ThenInclude(product => product!.Category)
            .AsNoTracking()
            .AsQueryable();

        if (productId is > 0)
        {
            query = query.Where(movement => movement.ProductId == productId);
        }

        return await query
            .OrderByDescending(movement => movement.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<(bool Succeeded, string Message)> StockInAsync(
        int productId,
        int quantity,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return AddStockMovementAsync(
            new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.StockIn,
                Quantity = quantity,
                Reason = reason
            },
            cancellationToken);
    }

    public Task<(bool Succeeded, string Message)> StockOutAsync(
        int productId,
        int quantity,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return AddStockMovementAsync(
            new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.StockOut,
                Quantity = quantity,
                Reason = reason
            },
            cancellationToken);
    }

    public async Task<(bool Succeeded, string Message)> AdjustStockAsync(
        int productId,
        int countedStock,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(
            item => item.Id == productId,
            cancellationToken);

        if (product is null)
        {
            return (false, "Choose a valid product.");
        }

        if (countedStock < 0)
        {
            return (false, "Counted stock cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return (false, "Reason is required.");
        }

        var difference = countedStock - product.CurrentStock;
        if (difference == 0)
        {
            return (true, "No adjustment needed. Counted stock already matches current stock.");
        }

        var now = DateTime.UtcNow;

        product.CurrentStock = countedStock;
        product.UpdatedAt = now;

        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            MovementType = StockMovementType.StockAdjustment,
            Quantity = Math.Abs(difference),
            Reason = reason.Trim(),
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Stock adjustment saved.");
    }

    public async Task<StockMovementSummary> GetStockMovementSummaryAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var movements = await db.StockMovements
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var lowStockProductCount = await db.Products
            .AsNoTracking()
            .CountAsync(product => product.CurrentStock <= product.ReorderLevel, cancellationToken);

        return new StockMovementSummary(
            movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockIn)
                .Sum(movement => movement.Quantity),
            movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockOut)
                .Sum(movement => movement.Quantity),
            movements
                .Count(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockAdjustment),
            movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockAdjustment)
                .Sum(movement => movement.Quantity),
            lowStockProductCount,
            movements.Count == 0 ? null : movements.Max(movement => movement.CreatedAt));
    }

    public async Task<(bool Succeeded, string Message)> AddStockMovementAsync(
        StockMovement movement,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(
            item => item.Id == movement.ProductId,
            cancellationToken);

        if (product is null)
        {
            return (false, "Choose a valid product.");
        }

        if (movement.Quantity <= 0)
        {
            return (false, "Quantity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(movement.Reason))
        {
            return (false, "Reason is required.");
        }

        var stockDelta = movement.MovementType switch
        {
            StockMovementType.StockIn => movement.Quantity,
            StockMovementType.StockOut => -movement.Quantity,
            StockMovementType.StockAdjustment => throw new InvalidOperationException("Use AdjustStockAsync for stock adjustments."),
            _ => throw new InvalidOperationException("Unknown stock movement type.")
        };

        if (product.CurrentStock + stockDelta < 0)
        {
            return (false, "Stock out cannot make current stock negative.");
        }

        product.CurrentStock += stockDelta;
        product.UpdatedAt = DateTime.UtcNow;

        movement.CreatedAt = DateTime.UtcNow;
        movement.Reason = movement.Reason.Trim();
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);

        return (true, "Stock movement saved.");
    }
}
