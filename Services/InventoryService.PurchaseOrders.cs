using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Services;

public partial class InventoryService
{
    public async Task<List<PurchaseOrder>> GetPurchaseOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await db.PurchaseOrders
            .Include(order => order.Supplier)
            .Include(order => order.Lines)
            .ThenInclude(line => line.Product)
            .OrderByDescending(order => order.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<PurchaseOrder?> GetPurchaseOrderAsync(int id, CancellationToken cancellationToken = default)
    {
        return await db.PurchaseOrders
            .Include(order => order.Supplier)
            .Include(order => order.Lines)
            .ThenInclude(line => line.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, PurchaseOrder? Order)> CreatePurchaseOrderAsync(
        int supplierId,
        IReadOnlyList<PurchaseOrderLineInput>? lines = null,
        CancellationToken cancellationToken = default)
    {
        var supplierExists = await db.Suppliers.AnyAsync(supplier => supplier.Id == supplierId, cancellationToken);
        if (!supplierExists)
        {
            return (false, "Choose a valid supplier.", null);
        }

        var now = DateTime.UtcNow;
        var order = new PurchaseOrder
        {
            SupplierId = supplierId,
            Status = PurchaseOrderStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.PurchaseOrders.Add(order);

        foreach (var line in lines ?? [])
        {
            var result = await AddOrUpdatePurchaseOrderLineCoreAsync(
                order,
                line.ProductId,
                line.OrderedQuantity,
                cancellationToken);

            if (!result.Succeeded)
            {
                return (false, result.Message, null);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order draft created.", order);
    }

    public async Task<(bool Succeeded, string Message)> AddOrUpdatePurchaseOrderLineAsync(
        int purchaseOrderId,
        int productId,
        int orderedQuantity,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .Include(value => value.Lines)
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        var result = await AddOrUpdatePurchaseOrderLineCoreAsync(
            order,
            productId,
            orderedQuantity,
            cancellationToken);

        if (!result.Succeeded)
        {
            return result;
        }

        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order line saved.");
    }

    public async Task<(bool Succeeded, string Message)> RemovePurchaseOrderLineAsync(
        int purchaseOrderId,
        int lineId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .Include(value => value.Lines)
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return (false, "Only draft purchase orders can be edited.");
        }

        var line = order.Lines.FirstOrDefault(value => value.Id == lineId);
        if (line is null)
        {
            return (false, "Purchase order line was not found.");
        }

        db.PurchaseOrderLines.Remove(line);
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order line removed.");
    }

    public async Task<(bool Succeeded, string Message)> MarkPurchaseOrderOrderedAsync(
        int purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .Include(value => value.Lines)
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return (false, "Only draft purchase orders can be marked ordered.");
        }

        if (order.Lines.Count == 0)
        {
            return (false, "Add at least one line before marking the order as ordered.");
        }

        if (order.Lines.Any(line => line.OrderedQuantity <= 0))
        {
            return (false, "Ordered quantity must be greater than zero.");
        }

        var now = DateTime.UtcNow;
        order.Status = PurchaseOrderStatus.Ordered;
        order.OrderedAt = now;
        order.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order marked as ordered.");
    }

    public async Task<(bool Succeeded, string Message)> ReceivePurchaseOrderAsync(
        int purchaseOrderId,
        IReadOnlyList<PurchaseOrderReceiveLineInput> receivedLines,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .Include(value => value.Lines)
            .ThenInclude(line => line.Product)
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        if (order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received)
        {
            return (false, "Cancelled or fully received purchase orders cannot receive stock.");
        }

        if (order.Status == PurchaseOrderStatus.Draft)
        {
            return (false, "Mark the purchase order as ordered before receiving stock.");
        }

        var positiveReceipts = receivedLines
            .Where(line => line.Quantity > 0)
            .ToList();

        if (positiveReceipts.Count == 0)
        {
            return (false, "Enter at least one received quantity.");
        }

        foreach (var receipt in positiveReceipts)
        {
            var line = order.Lines.FirstOrDefault(value => value.Id == receipt.LineId);
            if (line is null)
            {
                return (false, "Choose a valid purchase order line.");
            }

            var remaining = line.OrderedQuantity - line.ReceivedQuantity;
            if (receipt.Quantity > remaining)
            {
                return (false, "Received quantity cannot be greater than the open ordered quantity.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var receipt in positiveReceipts)
        {
            var line = order.Lines.First(value => value.Id == receipt.LineId);
            if (line.Product is null)
            {
                return (false, "Purchase order line product was not found.");
            }

            line.ReceivedQuantity += receipt.Quantity;
            line.Product.CurrentStock += receipt.Quantity;
            line.Product.UpdatedAt = now;

            db.StockMovements.Add(new StockMovement
            {
                ProductId = line.ProductId,
                MovementType = StockMovementType.StockIn,
                Quantity = receipt.Quantity,
                Reason = $"Purchase order #{order.Id} received",
                CreatedAt = now
            });
        }

        order.Status = order.Lines.All(line => line.ReceivedQuantity == line.OrderedQuantity)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        order.UpdatedAt = now;
        order.ReceivedAt = order.Status == PurchaseOrderStatus.Received ? now : null;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (true, order.Status == PurchaseOrderStatus.Received
            ? "Purchase order fully received."
            : "Purchase order partially received.");
    }

    public async Task<(bool Succeeded, string Message)> CancelPurchaseOrderAsync(
        int purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        if (order.Status == PurchaseOrderStatus.Received)
        {
            return (false, "Fully received purchase orders cannot be cancelled.");
        }

        if (order.Status == PurchaseOrderStatus.Cancelled)
        {
            return (false, "Purchase order is already cancelled.");
        }

        var now = DateTime.UtcNow;
        order.Status = PurchaseOrderStatus.Cancelled;
        order.CancelledAt = now;
        order.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order cancelled.");
    }

    private async Task<(bool Succeeded, string Message)> AddOrUpdatePurchaseOrderLineCoreAsync(
        PurchaseOrder order,
        int productId,
        int orderedQuantity,
        CancellationToken cancellationToken)
    {
        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return (false, "Only draft purchase orders can be edited.");
        }

        if (orderedQuantity <= 0)
        {
            return (false, "Ordered quantity must be greater than zero.");
        }

        var product = await db.Products.FirstOrDefaultAsync(
            value => value.Id == productId,
            cancellationToken);

        if (product is null)
        {
            return (false, "Choose a valid product.");
        }

        if (product.SupplierId != order.SupplierId)
        {
            return (false, "Product must belong to the purchase order supplier.");
        }

        var existingLine = order.Lines.FirstOrDefault(value => value.ProductId == productId);
        if (existingLine is null)
        {
            order.Lines.Add(new PurchaseOrderLine
            {
                ProductId = productId,
                OrderedQuantity = orderedQuantity,
                ReceivedQuantity = 0
            });
        }
        else
        {
            existingLine.OrderedQuantity = orderedQuantity;
        }

        return (true, "Purchase order line saved.");
    }
}
