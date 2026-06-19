using inventory_admin.Data;
using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace inventory_admin.Services;

public class InventoryService(AppDbContext db)
{
    public Task<List<Product>> GetProductsAsync(
        string? search = null,
        int? categoryId = null,
        int? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        return GetProductsAsync(
            new ProductListFilters(search, categoryId, supplierId),
            cancellationToken);
    }

    public async Task<List<Product>> GetProductsAsync(
        ProductListFilters? filters,
        CancellationToken cancellationToken = default)
    {
        var query = db.Products
            .Include(product => product.Category)
            .Include(product => product.Supplier)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters?.Search))
        {
            var normalizedSearch = filters.Search.Trim();
            query = query.Where(product =>
                product.Name.Contains(normalizedSearch) ||
                product.Sku.Contains(normalizedSearch));
        }

        if (filters?.CategoryId is > 0)
        {
            query = query.Where(product => product.CategoryId == filters.CategoryId);
        }

        if (filters?.SupplierId is > 0)
        {
            query = query.Where(product => product.SupplierId == filters.SupplierId);
        }

        if (filters?.LowStockOnly == true)
        {
            query = query.Where(product => product.CurrentStock <= product.ReorderLevel);
        }

        return await query
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default)
    {
        return await db.Products
            .Include(product => product.Category)
            .Include(product => product.Supplier)
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
    }

    public async Task<Product> SaveProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        if (product.CurrentStock < 0)
        {
            throw new InvalidOperationException("Current stock cannot be negative.");
        }

        if (product.ReorderLevel < 0)
        {
            throw new InvalidOperationException("Reorder level cannot be negative.");
        }

        if (product.UnitPrice < 0)
        {
            throw new InvalidOperationException("Unit price cannot be negative.");
        }

        product.Sku = product.Sku.Trim().ToUpperInvariant();
        product.Name = product.Name.Trim();
        product.Description = NormalizeOptional(product.Description);

        if (product.Id == 0)
        {
            product.CreatedAt = DateTime.UtcNow;
            product.UpdatedAt = DateTime.UtcNow;
            db.Products.Add(product);
        }
        else
        {
            var existing = await db.Products.FindAsync([product.Id], cancellationToken)
                ?? throw new InvalidOperationException("Product was not found.");

            existing.Name = product.Name;
            existing.Sku = product.Sku;
            existing.Description = product.Description;
            existing.CategoryId = product.CategoryId;
            existing.SupplierId = product.SupplierId;
            existing.ReorderLevel = product.ReorderLevel;
            existing.UnitPrice = product.UnitPrice;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task DeleteProductAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FindAsync([id], cancellationToken)
            ?? throw new InvalidOperationException("Product was not found.");

        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await db.Categories
            .Include(category => category.Products)
            .OrderBy(category => category.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Category?> GetCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        return await db.Categories.FindAsync([id], cancellationToken);
    }

    public async Task<Category> SaveCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        category.Name = category.Name.Trim();
        category.Description = NormalizeOptional(category.Description);

        if (category.Id == 0)
        {
            category.CreatedAt = DateTime.UtcNow;
            db.Categories.Add(category);
        }
        else
        {
            db.Categories.Update(category);
        }

        await db.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories
            .Include(value => value.Products)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Category was not found.");

        if (category.Products.Count > 0)
        {
            throw new InvalidOperationException("Cannot delete a category that still has products.");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }

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

    public async Task<List<StockMovement>> GetStockMovementsAsync(
        int? productId = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.StockMovements
            .Include(movement => movement.Product)
            .ThenInclude(product => product!.Category)
            .AsNoTracking()
            .AsQueryable();

        if (productId is > 0)
        {
            query = query.Where(movement => movement.ProductId == productId);
        }

        return await query
            .OrderByDescending(movement => movement.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<(bool Succeeded, string Message)> StockInAsync(
        int productId,
        int quantity,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return AddStockMovementAsync(
            new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.StockIn,
                Quantity = quantity,
                Reason = reason
            },
            cancellationToken);
    }

    public Task<(bool Succeeded, string Message)> StockOutAsync(
        int productId,
        int quantity,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return AddStockMovementAsync(
            new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.StockOut,
                Quantity = quantity,
                Reason = reason
            },
            cancellationToken);
    }

    public async Task<(bool Succeeded, string Message)> AdjustStockAsync(
        int productId,
        int countedStock,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(
            item => item.Id == productId,
            cancellationToken);

        if (product is null)
        {
            return (false, "Choose a valid product.");
        }

        if (countedStock < 0)
        {
            return (false, "Counted stock cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return (false, "Reason is required.");
        }

        var difference = countedStock - product.CurrentStock;
        if (difference == 0)
        {
            return (true, "No adjustment needed. Counted stock already matches current stock.");
        }

        var now = DateTime.UtcNow;

        product.CurrentStock = countedStock;
        product.UpdatedAt = now;

        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            MovementType = StockMovementType.StockAdjustment,
            Quantity = Math.Abs(difference),
            Reason = reason.Trim(),
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Stock adjustment saved.");
    }

    public async Task<StockMovementSummary> GetStockMovementSummaryAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var movements = await db.StockMovements
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var lowStockProductCount = await db.Products
            .AsNoTracking()
            .CountAsync(product => product.CurrentStock <= product.ReorderLevel, cancellationToken);

        return new StockMovementSummary(
            movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockIn)
                .Sum(movement => movement.Quantity),
            movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockOut)
                .Sum(movement => movement.Quantity),
            movements
                .Count(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockAdjustment),
            movements
                .Where(movement => movement.CreatedAt >= since && movement.MovementType == StockMovementType.StockAdjustment)
                .Sum(movement => movement.Quantity),
            lowStockProductCount,
            movements.Count == 0 ? null : movements.Max(movement => movement.CreatedAt));
    }

    public async Task<(bool Succeeded, string Message)> AddStockMovementAsync(
        StockMovement movement,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(
            item => item.Id == movement.ProductId,
            cancellationToken);

        if (product is null)
        {
            return (false, "Choose a valid product.");
        }

        if (movement.Quantity <= 0)
        {
            return (false, "Quantity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(movement.Reason))
        {
            return (false, "Reason is required.");
        }

        var stockDelta = movement.MovementType switch
        {
            StockMovementType.StockIn => movement.Quantity,
            StockMovementType.StockOut => -movement.Quantity,
            StockMovementType.StockAdjustment => throw new InvalidOperationException("Use AdjustStockAsync for stock adjustments."),
            _ => throw new InvalidOperationException("Unknown stock movement type.")
        };

        if (product.CurrentStock + stockDelta < 0)
        {
            return (false, "Stock out cannot make current stock negative.");
        }

        product.CurrentStock += stockDelta;
        product.UpdatedAt = DateTime.UtcNow;

        movement.CreatedAt = DateTime.UtcNow;
        movement.Reason = movement.Reason.Trim();
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);

        return (true, "Stock movement saved.");
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
