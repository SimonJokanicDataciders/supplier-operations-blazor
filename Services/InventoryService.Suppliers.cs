using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace inventory_admin.Services;

public partial class InventoryService
{
    public async Task<List<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default)
    {
        return await db.Suppliers
            .Include(supplier => supplier.Products)
            .OrderBy(supplier => supplier.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Supplier?> GetSupplierAsync(int id, CancellationToken cancellationToken = default)
    {
        return await db.Suppliers.FindAsync([id], cancellationToken);
    }

    public async Task<Supplier?> GetSupplierDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        return await db.Suppliers
            .Include(value => value.Products)
            .ThenInclude(product => product.Category)
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
    }

    public async Task<List<SupplierListItemDto>> GetSupplierListAsync(CancellationToken cancellationToken = default)
    {
        var suppliers = await db.Suppliers
            .Include(supplier => supplier.Products)
            .OrderBy(supplier => supplier.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return suppliers.Select(ToSupplierListItemDto).ToList();
    }

    public async Task<SupplierDetailDto?> GetSupplierDetailDtoAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var supplier = await db.Suppliers
            .Include(value => value.Products)
            .ThenInclude(product => product.Category)
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);

        if (supplier is null)
        {
            return null;
        }

        var recentMovements = await db.StockMovements
            .Include(movement => movement.Product)
            .Where(movement => movement.Product != null && movement.Product.SupplierId == id)
            .OrderByDescending(movement => movement.CreatedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var operationSummary = await GetSupplierOperationSummaryAsync(id, cancellationToken)
            ?? new SupplierOperationSummary
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.Name
            };

        return new SupplierDetailDto(
            supplier.Id,
            supplier.Name,
            supplier.CountryCode,
            supplier.ContactEmail,
            supplier.WebsiteUrl,
            supplier.Industry,
            supplier.OperationalNotes,
            supplier.SourceSystem,
            supplier.ExternalSupplierKey,
            supplier.ResearchUrl,
            supplier.ImportedAt,
            supplier.CreatedAt,
            supplier.UpdatedAt,
            operationSummary,
            supplier.Products
                .OrderBy(product => product.Name)
                .Select(product => new SupplierProductDto(
                    product.Id,
                    product.Name,
                    product.Sku,
                    product.Category?.Name,
                    product.CurrentStock,
                    product.ReorderLevel,
                    product.UnitPrice,
                    product.CurrentStock <= product.ReorderLevel))
                .ToList(),
            recentMovements.Select(ToSupplierRecentMovementDto).ToList());
    }

    public async Task<SupplierOperationSummary?> GetSupplierOperationSummaryAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var supplier = await db.Suppliers
            .Include(value => value.Products)
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);

        if (supplier is null)
        {
            return null;
        }

        var since = DateTime.UtcNow.AddDays(-30);
        var movements = await db.StockMovements
            .Include(movement => movement.Product)
            .Where(movement => movement.Product != null && movement.Product.SupplierId == id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new SupplierOperationSummary
        {
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            ProductCount = supplier.Products.Count,
            CurrentStock = supplier.Products.Sum(product => product.CurrentStock),
            LowStockCount = supplier.Products.Count(product => product.CurrentStock <= product.ReorderLevel),
            InventoryValue = supplier.Products.Sum(product => product.CurrentStock * product.UnitPrice),
            StockInQuantityLast30Days = movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockIn)
                .Sum(movement => movement.Quantity),
            StockOutQuantityLast30Days = movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockOut)
                .Sum(movement => movement.Quantity),
            AdjustmentCountLast30Days = movements
                .Count(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockAdjustment),
            AdjustmentQuantityLast30Days = movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockAdjustment)
                .Sum(movement => movement.Quantity),
            LatestMovementAt = movements.Count == 0 ? null : movements.Max(movement => movement.CreatedAt)
        };
    }

    public async Task<List<StockMovement>> GetRecentMovementsForSupplierAsync(
        int supplierId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        return await db.StockMovements
            .Include(movement => movement.Product)
            .Where(movement => movement.Product != null && movement.Product.SupplierId == supplierId)
            .OrderByDescending(movement => movement.CreatedAt)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierImportPreview> PreviewSupplierImportAsync(
        SupplierOperationsImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = NormalizeImportItem(ToImportItem(request));
        var errors = ValidateSupplierImportItem(item);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var existing = await FindImportMatchAsync(item, cancellationToken);

        return new SupplierImportPreview
        {
            Name = item.Name,
            CountryCode = item.CountryCode,
            ContactEmail = item.ContactEmail,
            WebsiteUrl = item.WebsiteUrl,
            Industry = item.Industry,
            SourceSystem = item.SourceSystem,
            ExternalSupplierKey = item.ExternalSupplierKey,
            MatchedExisting = existing is not null,
            ExistingSupplierId = existing?.Id,
            MatchMessage = existing is null
                ? "No matching supplier was found; import will create a new supplier."
                : $"Matched existing supplier #{existing.Id}; import will update it."
        };
    }

    public async Task<Supplier> ImportSupplierAsync(
        SupplierOperationsImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = NormalizeImportItem(ToImportItem(request));
        var errors = ValidateSupplierImportItem(item);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var now = DateTime.UtcNow;
        var supplier = await FindImportMatchAsync(item, cancellationToken);

        if (supplier is null)
        {
            supplier = new Supplier
            {
                CreatedAt = now
            };
            db.Suppliers.Add(supplier);
        }

        ApplyImportItem(supplier, item, now);
        await db.SaveChangesAsync(cancellationToken);

        return supplier;
    }

    public async Task<SupplierImportPreviewDto> PreviewSupplierImportAsync(
        SupplierImportRequest request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SupplierImportItemDto> suppliers = request.Suppliers ?? Array.Empty<SupplierImportItemDto>();
        var rows = new List<SupplierImportPreviewRowDto>();

        for (var index = 0; index < suppliers.Count; index++)
        {
            var item = NormalizeImportItem(suppliers[index]);
            var errors = ValidateSupplierImportItem(item);
            var existing = errors.Count == 0
                ? await FindImportMatchAsync(item, cancellationToken)
                : null;
            var action = errors.Count > 0 ? "Invalid" : existing is null ? "Create" : "Update";

            rows.Add(new SupplierImportPreviewRowDto(
                index + 1,
                item.Name,
                item.CountryCode,
                item.SourceSystem,
                item.ExternalSupplierKey,
                action,
                existing?.Id,
                errors));
        }

        return new SupplierImportPreviewDto(
            rows.Count,
            rows.Count(row => row.Action == "Create"),
            rows.Count(row => row.Action == "Update"),
            rows.Count(row => row.Action == "Invalid"),
            rows);
    }

    public async Task<SupplierImportResultDto> ImportSuppliersAsync(
        SupplierImportRequest request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SupplierImportItemDto> suppliers = request.Suppliers ?? Array.Empty<SupplierImportItemDto>();
        var rows = new List<SupplierImportResultRowDto>();
        var now = DateTime.UtcNow;
        var created = 0;
        var updated = 0;
        var skipped = 0;

        for (var index = 0; index < suppliers.Count; index++)
        {
            var rowNumber = index + 1;
            var item = NormalizeImportItem(suppliers[index]);
            var errors = ValidateSupplierImportItem(item);

            if (errors.Count > 0)
            {
                skipped++;
                rows.Add(new SupplierImportResultRowDto(rowNumber, item.Name, "Invalid", null, errors));
                continue;
            }

            var supplier = await FindImportMatchAsync(item, cancellationToken);
            var action = supplier is null ? "Created" : "Updated";

            if (supplier is null)
            {
                supplier = new Supplier
                {
                    CreatedAt = now
                };
                db.Suppliers.Add(supplier);
                created++;
            }
            else
            {
                updated++;
            }

            ApplyImportItem(supplier, item, now);
            await db.SaveChangesAsync(cancellationToken);
            rows.Add(new SupplierImportResultRowDto(rowNumber, supplier.Name, action, supplier.Id, []));
        }

        return new SupplierImportResultDto(suppliers.Count, created, updated, skipped, rows);
    }

    public async Task<Supplier> SaveSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        supplier.Name = supplier.Name.Trim();
        supplier.CountryCode = supplier.CountryCode.Trim().ToUpperInvariant();
        supplier.ContactEmail = NormalizeOptional(supplier.ContactEmail);
        supplier.WebsiteUrl = NormalizeOptional(supplier.WebsiteUrl);
        supplier.Industry = NormalizeOptional(supplier.Industry);
        supplier.OperationalNotes = NormalizeOptional(supplier.OperationalNotes);

        if (supplier.Id == 0)
        {
            supplier.CreatedAt = DateTime.UtcNow;
            supplier.UpdatedAt = DateTime.UtcNow;
            db.Suppliers.Add(supplier);
        }
        else
        {
            var existing = await db.Suppliers.FindAsync([supplier.Id], cancellationToken)
                ?? throw new InvalidOperationException("Supplier was not found.");

            existing.Name = supplier.Name;
            existing.CountryCode = supplier.CountryCode;
            existing.ContactEmail = supplier.ContactEmail;
            existing.WebsiteUrl = supplier.WebsiteUrl;
            existing.Industry = supplier.Industry;
            existing.OperationalNotes = supplier.OperationalNotes;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return supplier;
    }

    public async Task DeleteSupplierAsync(int id, CancellationToken cancellationToken = default)
    {
        var supplier = await db.Suppliers
            .Include(value => value.Products)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Supplier was not found.");

        if (supplier.Products.Count > 0)
        {
            throw new InvalidOperationException("Cannot delete a supplier that still has products.");
        }

        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync(cancellationToken);
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

    private static SupplierRecentMovementDto ToSupplierRecentMovementDto(StockMovement movement)
    {
        return new SupplierRecentMovementDto(
            movement.Id,
            movement.ProductId,
            movement.Product?.Name ?? string.Empty,
            movement.MovementType,
            movement.Quantity,
            movement.Reason,
            movement.CreatedAt);
    }

    private async Task<Supplier?> FindImportMatchAsync(
        SupplierImportItemDto item,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceSystem) && !string.IsNullOrWhiteSpace(item.ExternalSupplierKey))
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(
                value => value.SourceSystem == item.SourceSystem &&
                    value.ExternalSupplierKey == item.ExternalSupplierKey,
                cancellationToken);

            if (supplier is not null)
            {
                return supplier;
            }
        }

        return await db.Suppliers.FirstOrDefaultAsync(
            value => value.Name == item.Name && value.CountryCode == item.CountryCode,
            cancellationToken);
    }

    private static SupplierImportItemDto ToImportItem(SupplierOperationsImportRequest request)
    {
        var payload = TryReadPayload(request.PayloadJson);

        return new SupplierImportItemDto(
            payload.Name ?? request.Name ?? string.Empty,
            payload.CountryCode ?? request.CountryCode ?? string.Empty,
            payload.ContactEmail ?? request.ContactEmail,
            payload.WebsiteUrl ?? request.WebsiteUrl,
            payload.Industry ?? request.Industry,
            payload.OperationalNotes ?? request.OperationalNotes,
            payload.SourceSystem ?? request.SourceSystem ?? "Supplier Intelligence",
            payload.ExternalSupplierKey ?? request.ExternalSupplierKey,
            payload.ResearchUrl ?? request.ResearchUrl);
    }

    private static SupplierImportItemDto TryReadPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new SupplierImportItemDto(string.Empty, string.Empty, null, null, null, null, null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            return new SupplierImportItemDto(
                ReadString(root, "name", "supplierName", "legalName") ?? string.Empty,
                ReadString(root, "countryCode", "country_code", "country") ?? string.Empty,
                ReadString(root, "contactEmail", "contact_email", "email"),
                ReadString(root, "websiteUrl", "website_url", "website"),
                ReadString(root, "industry", "sector"),
                ReadString(root, "operationalNotes", "operational_notes", "notes"),
                ReadString(root, "sourceSystem", "source_system", "source"),
                ReadString(root, "externalSupplierKey", "external_supplier_key", "supplierKey", "supplier_id"),
                ReadString(root, "researchUrl", "research_url", "profileUrl", "profile_url"));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Supplier payload is not valid JSON.", ex);
        }
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return NormalizeOptional(property.GetString());
            }

            if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return property.ToString();
            }
        }

        return null;
    }

    private static SupplierImportItemDto NormalizeImportItem(SupplierImportItemDto item)
    {
        return item with
        {
            Name = item.Name?.Trim() ?? string.Empty,
            CountryCode = item.CountryCode?.Trim().ToUpperInvariant() ?? string.Empty,
            ContactEmail = NormalizeOptional(item.ContactEmail),
            WebsiteUrl = NormalizeOptional(item.WebsiteUrl),
            Industry = NormalizeOptional(item.Industry),
            OperationalNotes = NormalizeOptional(item.OperationalNotes),
            SourceSystem = NormalizeOptional(item.SourceSystem),
            ExternalSupplierKey = NormalizeOptional(item.ExternalSupplierKey),
            ResearchUrl = NormalizeOptional(item.ResearchUrl)
        };
    }

    private static List<string> ValidateSupplierImportItem(SupplierImportItemDto item)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(item.Name))
        {
            errors.Add("Name is required.");
        }

        if (item.Name.Length > 160)
        {
            errors.Add("Name cannot exceed 160 characters.");
        }

        if (item.CountryCode.Length != 2)
        {
            errors.Add("CountryCode must be exactly 2 characters.");
        }

        AddMaxLengthError(errors, item.ContactEmail, 254, nameof(item.ContactEmail));
        AddMaxLengthError(errors, item.WebsiteUrl, 500, nameof(item.WebsiteUrl));
        AddMaxLengthError(errors, item.Industry, 120, nameof(item.Industry));
        AddMaxLengthError(errors, item.OperationalNotes, 2000, nameof(item.OperationalNotes));
        AddMaxLengthError(errors, item.SourceSystem, 80, nameof(item.SourceSystem));
        AddMaxLengthError(errors, item.ExternalSupplierKey, 120, nameof(item.ExternalSupplierKey));
        AddMaxLengthError(errors, item.ResearchUrl, 500, nameof(item.ResearchUrl));

        return errors;
    }

    private static void AddMaxLengthError(List<string> errors, string? value, int maxLength, string fieldName)
    {
        if (value?.Length > maxLength)
        {
            errors.Add($"{fieldName} cannot exceed {maxLength} characters.");
        }
    }

    private static void ApplyImportItem(Supplier supplier, SupplierImportItemDto item, DateTime now)
    {
        supplier.Name = item.Name;
        supplier.CountryCode = item.CountryCode;
        supplier.ContactEmail = item.ContactEmail;
        supplier.WebsiteUrl = item.WebsiteUrl;
        supplier.Industry = item.Industry;
        supplier.OperationalNotes = item.OperationalNotes;
        supplier.SourceSystem = item.SourceSystem;
        supplier.ExternalSupplierKey = item.ExternalSupplierKey;
        supplier.ResearchUrl = item.ResearchUrl;
        supplier.ImportedAt = now;
        supplier.UpdatedAt = now;
    }
}
