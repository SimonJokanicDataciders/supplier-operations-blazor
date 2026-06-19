namespace inventory_admin.Models;

public sealed record DashboardSummary(
    int ProductCount,
    int SupplierCount,
    int LowStockCount,
    decimal InventoryValue,
    IReadOnlyList<Product> LowStockProducts,
    IReadOnlyList<StockMovement> RecentMovements,
    IReadOnlyList<SupplierDashboardRow> SupplierRows,
    inventory_admin.Dtos.SupplierDashboardOverviewDto SupplierOverview);
