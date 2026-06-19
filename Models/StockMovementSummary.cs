namespace inventory_admin.Models;

public sealed record StockMovementSummary(
    int StockInQuantityLast30Days,
    int StockOutQuantityLast30Days,
    int AdjustmentCountLast30Days,
    int AdjustmentQuantityLast30Days,
    int LowStockCount,
    DateTime? LatestMovementAt);
