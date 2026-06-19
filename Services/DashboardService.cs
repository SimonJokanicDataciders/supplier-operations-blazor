using inventory_admin.Data;
using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Services;

public class DashboardService(AppDbContext db)
{
    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var products = await db.Products
            .Include(product => product.Category)
            .Include(product => product.Supplier)
            .OrderBy(product => product.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var recentMovements = await db.StockMovements
            .Include(movement => movement.Product)
            .OrderByDescending(movement => movement.CreatedAt)
            .Take(8)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var lowStockProducts = products
            .Where(product => product.CurrentStock <= product.ReorderLevel)
            .OrderBy(product => product.CurrentStock)
            .ToList();

        var suppliers = await db.Suppliers
            .Include(supplier => supplier.Products)
            .OrderBy(supplier => supplier.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var supplierOverview = new SupplierDashboardOverviewDto(
            suppliers.Count,
            suppliers.Count(supplier => supplier.Products.Count > 0),
            suppliers.Count(supplier => supplier.Products.Any(product => product.CurrentStock <= product.ReorderLevel)),
            suppliers.Count(supplier => supplier.ImportedAt is not null),
            suppliers
                .Where(supplier => supplier.ImportedAt is not null)
                .Select(supplier => supplier.ImportedAt)
                .Max(),
            suppliers
                .Select(ToSupplierListItemDto)
                .OrderByDescending(supplier => supplier.InventoryValue)
                .Take(5)
                .ToList());

        var supplierRows = suppliers
            .Select(supplier => new SupplierDashboardRow
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.Name,
                CountryCode = supplier.CountryCode,
                Industry = supplier.Industry,
                ProductCount = supplier.Products.Count,
                LowStockCount = supplier.Products.Count(product => product.CurrentStock <= product.ReorderLevel),
                InventoryValue = supplier.Products.Sum(product => product.CurrentStock * product.UnitPrice),
                LatestMovementAt = recentMovements
                    .Where(movement => movement.Product?.SupplierId == supplier.Id)
                    .Select(movement => (DateTime?)movement.CreatedAt)
                    .Max()
            })
            .OrderByDescending(row => row.InventoryValue)
            .ThenBy(row => row.SupplierName)
            .ToList();

        return new DashboardSummary(
            products.Count,
            suppliers.Count,
            lowStockProducts.Count,
            products.Sum(product => product.CurrentStock * product.UnitPrice),
            lowStockProducts,
            recentMovements,
            supplierRows,
            supplierOverview);
    }

    private static SupplierListItemDto ToSupplierListItemDto(Supplier supplier)
    {
        return new SupplierListItemDto(
            supplier.Id,
            supplier.Name,
            supplier.CountryCode,
            supplier.ContactEmail,
            supplier.WebsiteUrl,
            supplier.Industry,
            supplier.SourceSystem,
            supplier.ExternalSupplierKey,
            supplier.ResearchUrl,
            supplier.ImportedAt,
            supplier.UpdatedAt,
            supplier.Products.Count,
            supplier.Products.Sum(product => product.CurrentStock),
            supplier.Products.Count(product => product.CurrentStock <= product.ReorderLevel),
            supplier.Products.Sum(product => product.CurrentStock * product.UnitPrice));
    }
}
