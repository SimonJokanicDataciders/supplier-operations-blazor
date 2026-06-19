using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Data;

public static class InventoryDataSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var hardware = new Category { Name = "Hardware", Description = "Physical inventory and devices." };
        var consumables = new Category { Name = "Consumables", Description = "Items regularly used and replenished." };
        var accessories = new Category { Name = "Accessories", Description = "Support parts and add-ons." };

        var acme = new Supplier
        {
            Name = "Acme Supply Co.",
            CountryCode = "US",
            ContactEmail = "orders@acme.example",
            WebsiteUrl = "https://acme.example",
            Industry = "Warehouse equipment",
            OperationalNotes = "Primary scanner and label supplier.",
            SourceSystem = "Seed",
            ExternalSupplierKey = "ACME-001",
            ResearchUrl = "https://acme.example/about",
            ImportedAt = DateTime.UtcNow
        };

        var nordic = new Supplier
        {
            Name = "Nordic Components",
            CountryCode = "SE",
            ContactEmail = "sales@nordic.example",
            WebsiteUrl = "https://nordic.example",
            Industry = "Industrial components",
            OperationalNotes = "Accessory and mounting hardware partner.",
            SourceSystem = "Seed",
            ExternalSupplierKey = "NORDIC-001",
            ResearchUrl = "https://nordic.example/company",
            ImportedAt = DateTime.UtcNow
        };

        var products = new List<Product>
        {
            new()
            {
                Name = "Barcode Scanner",
                Sku = "HW-SCAN-100",
                Description = "USB handheld scanner for warehouse intake.",
                Category = hardware,
                Supplier = acme,
                CurrentStock = 12,
                ReorderLevel = 5,
                UnitPrice = 89.90m
            },
            new()
            {
                Name = "Thermal Labels",
                Sku = "CON-LBL-040",
                Description = "Rolls of 40 mm thermal labels.",
                Category = consumables,
                Supplier = acme,
                CurrentStock = 3,
                ReorderLevel = 10,
                UnitPrice = 14.50m
            },
            new()
            {
                Name = "Mounting Bracket",
                Sku = "ACC-BRACKET-20",
                Description = "Wall bracket for scanner stations.",
                Category = accessories,
                Supplier = nordic,
                CurrentStock = 24,
                ReorderLevel = 8,
                UnitPrice = 9.75m
            }
        };

        db.Products.AddRange(products);
        await db.SaveChangesAsync(cancellationToken);

        db.StockMovements.AddRange(
            new StockMovement
            {
                ProductId = products[0].Id,
                MovementType = StockMovementType.StockIn,
                Quantity = 12,
                Reason = "Initial stock"
            },
            new StockMovement
            {
                ProductId = products[1].Id,
                MovementType = StockMovementType.StockIn,
                Quantity = 3,
                Reason = "Initial stock"
            },
            new StockMovement
            {
                ProductId = products[2].Id,
                MovementType = StockMovementType.StockIn,
                Quantity = 24,
                Reason = "Initial stock"
            });

        await db.SaveChangesAsync(cancellationToken);
    }
}
