using inventory_admin.Data;
using inventory_admin.Models;
using inventory_admin.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Tests;

internal sealed class InventoryServiceFixture : IAsyncDisposable
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
        await db.EnsureAdditiveSchemaAsync();

        return new InventoryServiceFixture(connection, db);
    }

    public async Task<Product> CreateProductAsync(int currentStock, int reorderLevel = 2)
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
            ReorderLevel = reorderLevel,
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
