namespace inventory_admin.Models;

public sealed record DashboardSummary(
    int ProductCount,
    int SupplierCount,
    int LowStockCount,
    int SuppliersWithLowStock,
    decimal InventoryValue,
    int StockInQuantityLast30Days,
    int StockOutQuantityLast30Days,
    int AdjustmentCountLast30Days,
    int AdjustmentQuantityLast30Days,
    IReadOnlyList<Product> LowStockProducts,
    IReadOnlyList<StockMovement> RecentMovements,
    IReadOnlyList<SupplierDashboardRow> SupplierRows,
    inventory_admin.Dtos.SupplierDashboardOverviewDto SupplierOverview,
    AiUsageSummary AiUsage);
