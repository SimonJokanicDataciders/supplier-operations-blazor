using inventory_admin.Data;

namespace inventory_admin.Services;

public partial class InventoryService
{
    private readonly AppDbContext db;

    public InventoryService(AppDbContext db)
    {
        this.db = db;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
