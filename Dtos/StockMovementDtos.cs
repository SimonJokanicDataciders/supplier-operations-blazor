namespace inventory_admin.Dtos;

public sealed record StockAdjustmentRequest(
    int ProductId,
    int CountedStock,
    string? Reason);
