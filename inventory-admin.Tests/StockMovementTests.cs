using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Tests;

public sealed class StockMovementTests
{
    [Fact]
    public async Task StockOutAsync_DoesNotAllowNegativeStock()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 3);

        var result = await fixture.Service.StockOutAsync(
            product.Id,
            quantity: 5,
            reason: "Too much usage");

        var savedProduct = await fixture.Db.Products.SingleAsync(item => item.Id == product.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("Stock out cannot make current stock negative.", result.Message);
        Assert.Equal(3, savedProduct.CurrentStock);
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task AdjustStockAsync_SetsFinalStockAndRecordsDifference()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 12);

        var result = await fixture.Service.AdjustStockAsync(
            product.Id,
            countedStock: 9,
            reason: "Cycle count");

        var savedProduct = await fixture.Db.Products.SingleAsync(item => item.Id == product.Id);
        var movement = await fixture.Db.StockMovements.SingleAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(9, savedProduct.CurrentStock);
        Assert.Equal(StockMovementType.StockAdjustment, movement.MovementType);
        Assert.Equal(3, movement.Quantity);
        Assert.Equal("Cycle count", movement.Reason);
    }

    [Fact]
    public async Task AdjustStockAsync_SameCountCreatesNoMovement()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 12);

        var result = await fixture.Service.AdjustStockAsync(
            product.Id,
            countedStock: 12,
            reason: "Cycle count");

        var savedProduct = await fixture.Db.Products.SingleAsync(item => item.Id == product.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("No adjustment needed. Counted stock already matches current stock.", result.Message);
        Assert.Equal(12, savedProduct.CurrentStock);
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }
}
