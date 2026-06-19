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

        var since = DateTime.UtcNow.AddDays(-30);
        var movementsLast30Days = await db.StockMovements
            .Where(movement => movement.CreatedAt >= since)
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

        var suppliersWithLowStock = suppliers.Count(supplier =>
            supplier.Products.Any(product => product.CurrentStock <= product.ReorderLevel));
        var aiUsage = await GetDashboardAiUsageAsync(cancellationToken);

        return new DashboardSummary(
            products.Count,
            suppliers.Count,
            lowStockProducts.Count,
            suppliersWithLowStock,
            products.Sum(product => product.CurrentStock * product.UnitPrice),
            movementsLast30Days
                .Where(movement => movement.MovementType == StockMovementType.StockIn)
                .Sum(movement => movement.Quantity),
            movementsLast30Days
                .Where(movement => movement.MovementType == StockMovementType.StockOut)
                .Sum(movement => movement.Quantity),
            movementsLast30Days
                .Count(movement => movement.MovementType == StockMovementType.StockAdjustment),
            movementsLast30Days
                .Where(movement => movement.MovementType == StockMovementType.StockAdjustment)
                .Sum(movement => movement.Quantity),
            lowStockProducts,
            recentMovements,
            supplierRows,
            supplierOverview,
            aiUsage);
    }

    private async Task<AiUsageSummary> GetDashboardAiUsageAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var records = await db.AiTokenUsageRecords
            .Where(record => record.CreatedAt >= monthStart)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var totalRequestCount = await db.AiTokenUsageRecords.CountAsync(cancellationToken);
        var budget = await db.AiMonthlyBudgets
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.MonthStart == monthStart, cancellationToken)
            ?? new AiMonthlyBudget
            {
                MonthStart = monthStart,
                BudgetUsd = 25m,
                WarningThresholdPercent = 80m,
                CriticalThresholdPercent = 100m
            };

        var effectiveSpend = records.Sum(record => record.EffectiveCostUsd);
        var budgetUsedPercent = budget.BudgetUsd == 0
            ? 0
            : Math.Round(effectiveSpend / budget.BudgetUsd * 100m, 2, MidpointRounding.AwayFromZero);
        var featureRows = BuildAiUsageGroupRows(records, record => record.FeatureName);
        var providerRows = BuildAiUsageGroupRows(records, record =>
            string.IsNullOrWhiteSpace(record.BillingProvider) ? record.Provider : record.BillingProvider);
        var modelRows = BuildAiUsageGroupRows(records, record =>
            string.IsNullOrWhiteSpace(record.RouteName)
                ? $"{record.Provider} / {record.ModelName}"
                : $"{record.Provider} / {record.ModelName} / {record.RouteName}");

        return new AiUsageSummary(
            totalRequestCount,
            records.Count,
            records.Sum(record => record.TotalTokens),
            records.Sum(record => record.EstimatedCostUsd),
            effectiveSpend,
            budget.BudgetUsd,
            budget.WarningThresholdPercent,
            budgetUsedPercent,
            budget.BudgetUsd > 0 &&
                effectiveSpend >= budget.BudgetUsd * budget.WarningThresholdPercent / 100m &&
                effectiveSpend <= budget.BudgetUsd,
            budget.BudgetUsd > 0 && effectiveSpend > budget.BudgetUsd,
            modelRows.FirstOrDefault()?.Name,
            featureRows.FirstOrDefault()?.Name,
            records.Count == 0 ? null : records.Max(record => record.CreatedAt),
            featureRows,
            providerRows,
            modelRows);
    }

    private static IReadOnlyList<AiUsageGroupSummary> BuildAiUsageGroupRows(
        IReadOnlyList<AiTokenUsageRecord> records,
        Func<AiTokenUsageRecord, string> keySelector)
    {
        return records
            .GroupBy(record =>
            {
                var key = keySelector(record);
                return string.IsNullOrWhiteSpace(key) ? "Unassigned" : key;
            })
            .Select(group => new AiUsageGroupSummary(
                group.Key,
                group.Count(),
                group.Sum(record => record.TotalTokens),
                group.Sum(record => record.EstimatedCostUsd),
                group.Sum(record => record.EffectiveCostUsd)))
            .OrderByDescending(row => row.ActualCostUsd)
            .ThenBy(row => row.Name)
            .ToList();
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
