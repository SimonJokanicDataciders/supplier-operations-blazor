namespace inventory_admin.Models;

public class SupplierOperationSummary
{
    public int SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public int ProductCount { get; set; }

    public int CurrentStock { get; set; }

    public int LowStockCount { get; set; }

    public decimal InventoryValue { get; set; }

    public int StockInQuantityLast30Days { get; set; }

    public int StockOutQuantityLast30Days { get; set; }

    public int AdjustmentCountLast30Days { get; set; }

    public int AdjustmentQuantityLast30Days { get; set; }

    public DateTime? LatestMovementAt { get; set; }
}
