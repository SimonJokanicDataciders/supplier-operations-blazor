using inventory_admin.Data;
using inventory_admin.Dtos;
using inventory_admin.Models;
using inventory_admin.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Tests;

public sealed class InventoryServiceTests
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

    [Fact]
    public async Task ImportSuppliersAsync_UpdatesExistingSupplierByExternalKey()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        fixture.Db.Suppliers.Add(new Supplier
        {
            Name = "Old Supplier Name",
            CountryCode = "AT",
            SourceSystem = "SupplierIntelligence",
            ExternalSupplierKey = "supplier-123",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.ImportSuppliersAsync(new SupplierImportRequest(
            [
                new SupplierImportItemDto(
                    "Updated Supplier",
                    "DE",
                    "ops@example.com",
                    "https://supplier.example",
                    "Bearings",
                    "Imported from research",
                    "SupplierIntelligence",
                    "supplier-123",
                    "https://research.example")
            ]));

        var suppliers = await fixture.Db.Suppliers.ToListAsync();
        var supplier = Assert.Single(suppliers);

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal("Updated Supplier", supplier.Name);
        Assert.Equal("DE", supplier.CountryCode);
        Assert.Equal("ops@example.com", supplier.ContactEmail);
    }

    private sealed class InventoryServiceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private InventoryServiceFixture(SqliteConnection connection, AppDbContext db)
        {
            this.connection = connection;
            Db = db;
            Service = new InventoryService(db);
        }

        public AppDbContext Db { get; }

        public InventoryService Service { get; }

        public static async Task<InventoryServiceFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            return new InventoryServiceFixture(connection, db);
        }

        public async Task<Product> CreateProductAsync(int currentStock)
        {
            var category = new Category
            {
                Name = "Hardware"
            };
            var supplier = new Supplier
            {
                Name = "Test Supplier",
                CountryCode = "AT"
            };
            var product = new Product
            {
                Name = "Test Product",
                Sku = Guid.NewGuid().ToString("N")[..12],
                Category = category,
                Supplier = supplier,
                CurrentStock = currentStock,
                ReorderLevel = 2,
                UnitPrice = 10
            };

            Db.Products.Add(product);
            await Db.SaveChangesAsync();

            return product;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
