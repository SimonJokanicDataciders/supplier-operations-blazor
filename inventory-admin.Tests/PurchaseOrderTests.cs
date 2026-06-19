using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Tests;

public sealed class PurchaseOrderTests
{
    [Fact]
    public async Task CreatePurchaseOrderAsync_StoresSupplierAndLines()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);

        var result = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);

        var order = await fixture.Db.PurchaseOrders
            .Include(value => value.Lines)
            .SingleAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(product.SupplierId, order.SupplierId);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        var line = Assert.Single(order.Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(8, line.OrderedQuantity);
        Assert.Equal(0, line.ReceivedQuantity);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_RejectsZeroQuantity()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);

        var result = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 0)]);

        Assert.False(result.Succeeded);
        Assert.Equal("Ordered quantity must be greater than zero.", result.Message);
        Assert.Empty(await fixture.Db.PurchaseOrders.ToListAsync());
    }

    [Fact]
    public async Task MarkPurchaseOrderOrderedAsync_MovesDraftToOrdered()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);

        var result = await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);

        var order = await fixture.Db.PurchaseOrders.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(PurchaseOrderStatus.Ordered, order.Status);
        Assert.NotNull(order.OrderedAt);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_DoesNotAllowMoreThanOpenQuantity()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 9)]);

        var savedProduct = await fixture.Db.Products.SingleAsync(value => value.Id == product.Id);
        Assert.False(result.Succeeded);
        Assert.Equal("Received quantity cannot be greater than the open ordered quantity.", result.Message);
        Assert.Equal(2, savedProduct.CurrentStock);
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_PartialReceiveUpdatesStatus()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 3)]);

        var order = await fixture.Db.PurchaseOrders.SingleAsync();
        var savedLine = await fixture.Db.PurchaseOrderLines.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
        Assert.Equal(3, savedLine.ReceivedQuantity);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_FullReceiveUpdatesStatus()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 8)]);

        var order = await fixture.Db.PurchaseOrders.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.NotNull(order.ReceivedAt);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_CreatesStockInMovementAndIncreasesStock()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 5)]);

        var savedProduct = await fixture.Db.Products.SingleAsync(value => value.Id == product.Id);
        var movement = await fixture.Db.StockMovements.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(7, savedProduct.CurrentStock);
        Assert.Equal(StockMovementType.StockIn, movement.MovementType);
        Assert.Equal(5, movement.Quantity);
        Assert.Equal($"Purchase order #{create.Order.Id} received", movement.Reason);
    }

    [Fact]
    public async Task CancelledPurchaseOrder_CannotBeReceived()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        await fixture.Service.CancelPurchaseOrderAsync(create.Order.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 1)]);

        Assert.False(result.Succeeded);
        Assert.Equal("Cancelled or fully received purchase orders cannot receive stock.", result.Message);
    }

    [Fact]
    public async Task LowStockPurchaseOrderCreateFlow_StoresSelectedSupplierAndProduct()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 1, reorderLevel: 6);
        var suggestedQuantity = Math.Max(product.ReorderLevel - product.CurrentStock, 1);

        var result = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, suggestedQuantity)]);

        var order = await fixture.Db.PurchaseOrders
            .Include(value => value.Lines)
            .SingleAsync();
        var line = Assert.Single(order.Lines);

        Assert.True(result.Succeeded);
        Assert.Equal(product.SupplierId, order.SupplierId);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(5, line.OrderedQuantity);
    }
}
