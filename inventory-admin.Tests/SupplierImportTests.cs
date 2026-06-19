using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Tests;

public sealed class SupplierImportTests
{
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
}
