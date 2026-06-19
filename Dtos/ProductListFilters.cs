namespace inventory_admin.Dtos;

public sealed record ProductListFilters(
    string? Search = null,
    int? CategoryId = null,
    int? SupplierId = null,
    bool LowStockOnly = false);
