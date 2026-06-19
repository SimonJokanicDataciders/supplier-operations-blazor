using inventory_admin.Models;

namespace inventory_admin.Dtos;

public sealed record SupplierListItemDto(
    int Id,
    string Name,
    string CountryCode,
    string? ContactEmail,
    string? WebsiteUrl,
    string? Industry,
    string? SourceSystem,
    string? ExternalSupplierKey,
    string? ResearchUrl,
    DateTime? ImportedAt,
    DateTime UpdatedAt,
    int ProductCount,
    int CurrentStock,
    int LowStockProductCount,
    decimal InventoryValue);

public sealed record SupplierDetailDto(
    int Id,
    string Name,
    string CountryCode,
    string? ContactEmail,
    string? WebsiteUrl,
    string? Industry,
    string? OperationalNotes,
    string? SourceSystem,
    string? ExternalSupplierKey,
    string? ResearchUrl,
    DateTime? ImportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    SupplierOperationSummary OperationSummary,
    IReadOnlyList<SupplierProductDto> Products,
    IReadOnlyList<SupplierRecentMovementDto> RecentMovements);

public sealed record SupplierProductDto(
    int Id,
    string Name,
    string Sku,
    string? Category,
    int CurrentStock,
    int ReorderLevel,
    decimal UnitPrice,
    bool IsLowStock);

public sealed record SupplierRecentMovementDto(
    int Id,
    int ProductId,
    string ProductName,
    StockMovementType MovementType,
    int Quantity,
    string Reason,
    DateTime CreatedAt);

public sealed record SupplierDashboardOverviewDto(
    int SupplierCount,
    int SuppliersWithProducts,
    int SuppliersWithLowStock,
    int ImportedSupplierCount,
    DateTime? LastImportAt,
    IReadOnlyList<SupplierListItemDto> TopSuppliersByInventoryValue);

public sealed record SupplierImportRequest(IReadOnlyList<SupplierImportItemDto>? Suppliers);

public sealed record SupplierImportItemDto(
    string Name,
    string CountryCode,
    string? ContactEmail,
    string? WebsiteUrl,
    string? Industry,
    string? OperationalNotes,
    string? SourceSystem,
    string? ExternalSupplierKey,
    string? ResearchUrl);

public sealed record SupplierImportPreviewDto(
    int TotalRows,
    int CreateCount,
    int UpdateCount,
    int InvalidCount,
    IReadOnlyList<SupplierImportPreviewRowDto> Rows);

public sealed record SupplierImportPreviewRowDto(
    int RowNumber,
    string Name,
    string CountryCode,
    string? SourceSystem,
    string? ExternalSupplierKey,
    string Action,
    int? ExistingSupplierId,
    IReadOnlyList<string> Errors);

public sealed record SupplierImportResultDto(
    int TotalRows,
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    IReadOnlyList<SupplierImportResultRowDto> Rows);

public sealed record SupplierImportResultRowDto(
    int RowNumber,
    string Name,
    string Action,
    int? SupplierId,
    IReadOnlyList<string> Errors);
