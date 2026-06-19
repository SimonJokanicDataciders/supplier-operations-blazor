namespace inventory_admin.Models;

public class SupplierDashboardRow
{
    public int SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;

    public string? Industry { get; set; }

    public int ProductCount { get; set; }

    public int LowStockCount { get; set; }

    public decimal InventoryValue { get; set; }

    public DateTime? LatestMovementAt { get; set; }
}
