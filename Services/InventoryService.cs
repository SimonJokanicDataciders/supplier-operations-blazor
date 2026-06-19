using inventory_admin.Data;
using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
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

    public async Task<List<AiTokenUsageRecord>> GetAiTokenUsageRecordsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.AiTokenUsageRecords
            .Include(record => record.AiModelPrice)
            .OrderByDescending(record => record.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AiModelPrice>> GetAiModelPricesAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.AiModelPrices
            .OrderByDescending(price => price.IsDefault)
            .ThenBy(price => price.Provider)
            .ThenBy(price => price.ModelName)
            .ThenBy(price => price.RouteName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AiMonthlyBudget>> GetAiMonthlyBudgetsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.AiMonthlyBudgets
            .OrderByDescending(budget => budget.MonthStart)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, AiMonthlyBudget? Budget)> SaveAiMonthlyBudgetAsync(
        AiMonthlyBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.BudgetUsd < 0)
        {
            return (false, "Monthly budget cannot be negative.", null);
        }

        if (request.WarningThresholdPercent < 0 || request.WarningThresholdPercent > 100 ||
            request.CriticalThresholdPercent < 0 || request.CriticalThresholdPercent > 100)
        {
            return (false, "Budget thresholds must be between 0 and 100 percent.", null);
        }

        if (request.WarningThresholdPercent > request.CriticalThresholdPercent)
        {
            return (false, "Warning threshold cannot exceed critical threshold.", null);
        }

        var monthStart = ToMonthStart(request.MonthStart);
        var now = DateTime.UtcNow;
        var budget = await db.AiMonthlyBudgets
            .FirstOrDefaultAsync(value => value.MonthStart == monthStart, cancellationToken);

        if (budget is null)
        {
            budget = new AiMonthlyBudget
            {
                MonthStart = monthStart,
                CreatedAt = now
            };
            db.AiMonthlyBudgets.Add(budget);
        }

        budget.BudgetUsd = request.BudgetUsd;
        budget.WarningThresholdPercent = request.WarningThresholdPercent;
        budget.CriticalThresholdPercent = request.CriticalThresholdPercent;
        budget.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return (true, "AI monthly budget saved.", budget);
    }

    public async Task<AiUsageSummaryDto> GetAiUsageSummaryAsync(
        DateTime? monthStart = null,
        CancellationToken cancellationToken = default)
    {
        var periodStart = ToMonthStart(monthStart ?? DateTime.UtcNow);
        var periodEnd = periodStart.AddMonths(1);

        var records = await db.AiTokenUsageRecords
            .Where(record => record.CreatedAt >= periodStart && record.CreatedAt < periodEnd)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var budget = await db.AiMonthlyBudgets
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.MonthStart == periodStart, cancellationToken);

        var estimated = records.Sum(record => record.EstimatedCostUsd);
        var actual = records.Sum(record => record.ActualCostUsd ?? 0m);
        var effective = records.Sum(record => record.EffectiveCostUsd);

        return new AiUsageSummaryDto(
            periodStart,
            periodEnd,
            records.Count,
            records.Sum(record => record.PromptTokens),
            records.Sum(record => record.CachedInputTokens),
            records.Sum(record => record.CompletionTokens),
            records.Sum(record => record.ReasoningTokens),
            records.Sum(record => record.TotalTokens),
            records.Sum(record => record.ToolCallCount),
            records.Sum(record => record.SearchCallCount),
            estimated,
            actual,
            effective,
            actual - estimated,
            budget is null ? null : ToBudgetStatus(budget, effective),
            BuildUsageGroups(records, "feature"),
            BuildUsageGroups(records, "provider"),
            BuildUsageGroups(records, "model"),
            BuildUsageGroups(records, "route"));
    }

    public async Task<(bool Succeeded, string Message, AiModelPrice? Price)> SaveAiModelPriceAsync(
        AiModelPrice price,
        CancellationToken cancellationToken = default)
    {
        price.Provider = price.Provider.Trim();
        price.BillingProvider = NormalizeOptional(price.BillingProvider);
        price.UpstreamProvider = NormalizeOptional(price.UpstreamProvider);
        price.ModelName = price.ModelName.Trim();
        price.OpenRouterModelSlug = NormalizeOptional(price.OpenRouterModelSlug);
        price.RouteName = NormalizeOptional(price.RouteName) ?? "Custom";
        price.Currency = string.IsNullOrWhiteSpace(price.Currency) ? "USD" : price.Currency.Trim().ToUpperInvariant();
        price.SourceUrl = NormalizeOptional(price.SourceUrl);

        if (string.IsNullOrWhiteSpace(price.Provider))
        {
            return (false, "Provider is required.", null);
        }

        if (string.IsNullOrWhiteSpace(price.ModelName))
        {
            return (false, "Model name is required.", null);
        }

        if (price.Currency.Length != 3)
        {
            return (false, "Currency must be a 3-letter code.", null);
        }

        if (price.InputCostPerMillionTokens < 0 ||
            price.CachedInputCostPerMillionTokens < 0 ||
            price.OutputCostPerMillionTokens < 0 ||
            price.SearchCostPerThousandCalls < 0)
        {
            return (false, "Prices cannot be negative.", null);
        }

        var now = DateTime.UtcNow;
        price.EffectiveFrom = price.EffectiveFrom == default ? now.Date : price.EffectiveFrom;
        price.CreatedAt = now;
        price.UpdatedAt = now;
        price.IsCustom = true;
        price.IsDefault = false;

        db.AiModelPrices.Add(price);
        await db.SaveChangesAsync(cancellationToken);

        return (true, "AI model price saved.", price);
    }

    public Task<(bool Succeeded, string Message, AiModelPrice? Price)> SaveAiModelPriceAsync(
        AiModelPriceCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return SaveAiModelPriceAsync(
            new AiModelPrice
            {
                Provider = request.Provider,
                BillingProvider = request.BillingProvider,
                UpstreamProvider = request.UpstreamProvider,
                ModelName = request.ModelName,
                OpenRouterModelSlug = request.OpenRouterModelSlug,
                RouteName = request.RouteName,
                InputCostPerMillionTokens = request.InputCostPerMillionTokens,
                CachedInputCostPerMillionTokens = request.CachedInputCostPerMillionTokens,
                OutputCostPerMillionTokens = request.OutputCostPerMillionTokens,
                SearchCostPerThousandCalls = request.SearchCostPerThousandCalls,
                EffectiveFrom = request.EffectiveFrom ?? DateTime.UtcNow.Date,
                SourceUrl = request.SourceUrl
            },
            cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, AiTokenUsageRecord? Record)> SaveAiTokenUsageRecordAsync(
        AiTokenUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        record.FeatureName = record.FeatureName.Trim();
        record.Provider = string.IsNullOrWhiteSpace(record.Provider) ? "OpenAI" : record.Provider.Trim();
        record.BillingProvider = string.IsNullOrWhiteSpace(record.BillingProvider)
            ? record.Provider
            : record.BillingProvider.Trim();
        record.UpstreamProvider = NormalizeOptional(record.UpstreamProvider);
        record.ModelName = record.ModelName.Trim();
        record.OpenRouterModelSlug = NormalizeOptional(record.OpenRouterModelSlug);
        record.RouteName = NormalizeOptional(record.RouteName);
        record.Notes = NormalizeOptional(record.Notes);

        if (record.AiModelPriceId is > 0)
        {
            var price = await db.AiModelPrices
                .AsNoTracking()
                .FirstOrDefaultAsync(value => value.Id == record.AiModelPriceId, cancellationToken);

            if (price is null)
            {
                return (false, "Choose a valid AI model price.", null);
            }

            ApplyPriceSnapshot(record, price);
        }

        if (string.IsNullOrWhiteSpace(record.FeatureName))
        {
            return (false, "Feature name is required.", null);
        }

        if (string.IsNullOrWhiteSpace(record.ModelName))
        {
            return (false, "Model name is required.", null);
        }

        if (record.PromptTokens < 0 ||
            record.CompletionTokens < 0 ||
            record.CachedInputTokens < 0 ||
            record.ReasoningTokens < 0 ||
            record.ToolCallCount < 0 ||
            record.SearchCallCount < 0)
        {
            return (false, "Usage counts cannot be negative.", null);
        }

        if (record.PromptTokens + record.CachedInputTokens + record.CompletionTokens + record.SearchCallCount == 0 &&
            (record.ActualCostUsd ?? 0m) == 0)
        {
            return (false, "Enter at least one token, billable search call, or actual cost.", null);
        }

        if (record.PromptCostPerMillionTokens < 0 ||
            record.CachedInputCostPerMillionTokens < 0 ||
            record.CompletionCostPerMillionTokens < 0 ||
            record.SearchCostPerThousandCalls < 0 ||
            record.ActualCostUsd < 0)
        {
            return (false, "Prices cannot be negative.", null);
        }

        record.EstimatedCostUsd = CalculateTokenCost(
            record.PromptTokens,
            record.CachedInputTokens,
            record.CompletionTokens,
            record.SearchCallCount,
            record.PromptCostPerMillionTokens,
            record.CachedInputCostPerMillionTokens,
            record.CompletionCostPerMillionTokens,
            record.SearchCostPerThousandCalls);
        if (record.CreatedAt == default)
        {
            record.CreatedAt = DateTime.UtcNow;
        }

        db.AiTokenUsageRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        return (true, "AI usage record saved.", record);
    }

    public Task<(bool Succeeded, string Message, AiTokenUsageRecord? Record)> SaveAiTokenUsageRecordAsync(
        AiTokenUsageCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return SaveAiTokenUsageRecordAsync(
            new AiTokenUsageRecord
            {
                FeatureName = request.FeatureName,
                Provider = request.Provider,
                BillingProvider = request.BillingProvider,
                UpstreamProvider = request.UpstreamProvider,
                ModelName = request.ModelName,
                OpenRouterModelSlug = request.OpenRouterModelSlug,
                RouteName = request.RouteName,
                AiModelPriceId = request.AiModelPriceId,
                PromptTokens = request.PromptTokens,
                CachedInputTokens = request.CachedInputTokens,
                CompletionTokens = request.CompletionTokens,
                ReasoningTokens = request.ReasoningTokens,
                ToolCallCount = request.ToolCallCount,
                SearchCallCount = request.SearchCallCount,
                PromptCostPerMillionTokens = request.PromptCostPerMillionTokens,
                CachedInputCostPerMillionTokens = request.CachedInputCostPerMillionTokens,
                CompletionCostPerMillionTokens = request.CompletionCostPerMillionTokens,
                SearchCostPerThousandCalls = request.SearchCostPerThousandCalls,
                ActualCostUsd = request.ActualCostUsd,
                CreatedAt = request.CreatedAt ?? DateTime.UtcNow,
                Notes = request.Notes
            },
            cancellationToken);
    }

    public async Task<AiTokenUsageImportResult> ImportAiTokenUsageAsync(
        AiTokenUsageImportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return new AiTokenUsageImportResult(0, [new AiTokenUsageImportError(0, "Paste CSV or JSON usage data to import.")]);
        }

        var errors = new List<AiTokenUsageImportError>();
        var rows = ParseAiUsageImportRows(request.Content, request.Format, errors);
        var imported = 0;

        foreach (var row in rows)
        {
            if (!TryCreateAiTokenUsageRecord(row, errors, out var record))
            {
                continue;
            }

            var result = await SaveAiTokenUsageRecordAsync(record, cancellationToken);

            if (result.Succeeded)
            {
                imported++;
            }
            else
            {
                errors.Add(new AiTokenUsageImportError(row.RowNumber, result.Message));
            }
        }

        return new AiTokenUsageImportResult(imported, errors);
    }

    public async Task<List<PurchaseOrder>> GetPurchaseOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await db.PurchaseOrders
            .Include(order => order.Supplier)
            .Include(order => order.Lines)
            .ThenInclude(line => line.Product)
            .OrderByDescending(order => order.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<PurchaseOrder?> GetPurchaseOrderAsync(int id, CancellationToken cancellationToken = default)
    {
        return await db.PurchaseOrders
            .Include(order => order.Supplier)
            .Include(order => order.Lines)
            .ThenInclude(line => line.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, PurchaseOrder? Order)> CreatePurchaseOrderAsync(
        int supplierId,
        IReadOnlyList<PurchaseOrderLineInput>? lines = null,
        CancellationToken cancellationToken = default)
    {
        var supplierExists = await db.Suppliers.AnyAsync(supplier => supplier.Id == supplierId, cancellationToken);
        if (!supplierExists)
        {
            return (false, "Choose a valid supplier.", null);
        }

        var now = DateTime.UtcNow;
        var order = new PurchaseOrder
        {
            SupplierId = supplierId,
            Status = PurchaseOrderStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.PurchaseOrders.Add(order);

        foreach (var line in lines ?? [])
        {
            var result = await AddOrUpdatePurchaseOrderLineCoreAsync(
                order,
                line.ProductId,
                line.OrderedQuantity,
                cancellationToken);

            if (!result.Succeeded)
            {
                return (false, result.Message, null);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order draft created.", order);
    }

    public async Task<(bool Succeeded, string Message)> AddOrUpdatePurchaseOrderLineAsync(
        int purchaseOrderId,
        int productId,
        int orderedQuantity,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .Include(value => value.Lines)
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        var result = await AddOrUpdatePurchaseOrderLineCoreAsync(
            order,
            productId,
            orderedQuantity,
            cancellationToken);

        if (!result.Succeeded)
        {
            return result;
        }

        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order line saved.");
    }

    public async Task<(bool Succeeded, string Message)> RemovePurchaseOrderLineAsync(
        int purchaseOrderId,
        int lineId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .Include(value => value.Lines)
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return (false, "Only draft purchase orders can be edited.");
        }

        var line = order.Lines.FirstOrDefault(value => value.Id == lineId);
        if (line is null)
        {
            return (false, "Purchase order line was not found.");
        }

        db.PurchaseOrderLines.Remove(line);
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order line removed.");
    }

    public async Task<(bool Succeeded, string Message)> MarkPurchaseOrderOrderedAsync(
        int purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .Include(value => value.Lines)
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return (false, "Only draft purchase orders can be marked ordered.");
        }

        if (order.Lines.Count == 0)
        {
            return (false, "Add at least one line before marking the order as ordered.");
        }

        if (order.Lines.Any(line => line.OrderedQuantity <= 0))
        {
            return (false, "Ordered quantity must be greater than zero.");
        }

        var now = DateTime.UtcNow;
        order.Status = PurchaseOrderStatus.Ordered;
        order.OrderedAt = now;
        order.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order marked as ordered.");
    }

    public async Task<(bool Succeeded, string Message)> ReceivePurchaseOrderAsync(
        int purchaseOrderId,
        IReadOnlyList<PurchaseOrderReceiveLineInput> receivedLines,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .Include(value => value.Lines)
            .ThenInclude(line => line.Product)
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        if (order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received)
        {
            return (false, "Cancelled or fully received purchase orders cannot receive stock.");
        }

        if (order.Status == PurchaseOrderStatus.Draft)
        {
            return (false, "Mark the purchase order as ordered before receiving stock.");
        }

        var positiveReceipts = receivedLines
            .Where(line => line.Quantity > 0)
            .ToList();

        if (positiveReceipts.Count == 0)
        {
            return (false, "Enter at least one received quantity.");
        }

        foreach (var receipt in positiveReceipts)
        {
            var line = order.Lines.FirstOrDefault(value => value.Id == receipt.LineId);
            if (line is null)
            {
                return (false, "Choose a valid purchase order line.");
            }

            var remaining = line.OrderedQuantity - line.ReceivedQuantity;
            if (receipt.Quantity > remaining)
            {
                return (false, "Received quantity cannot be greater than the open ordered quantity.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var receipt in positiveReceipts)
        {
            var line = order.Lines.First(value => value.Id == receipt.LineId);
            if (line.Product is null)
            {
                return (false, "Purchase order line product was not found.");
            }

            line.ReceivedQuantity += receipt.Quantity;
            line.Product.CurrentStock += receipt.Quantity;
            line.Product.UpdatedAt = now;

            db.StockMovements.Add(new StockMovement
            {
                ProductId = line.ProductId,
                MovementType = StockMovementType.StockIn,
                Quantity = receipt.Quantity,
                Reason = $"Purchase order #{order.Id} received",
                CreatedAt = now
            });
        }

        order.Status = order.Lines.All(line => line.ReceivedQuantity == line.OrderedQuantity)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        order.UpdatedAt = now;
        order.ReceivedAt = order.Status == PurchaseOrderStatus.Received ? now : null;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (true, order.Status == PurchaseOrderStatus.Received
            ? "Purchase order fully received."
            : "Purchase order partially received.");
    }

    public async Task<(bool Succeeded, string Message)> CancelPurchaseOrderAsync(
        int purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .FirstOrDefaultAsync(value => value.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return (false, "Purchase order was not found.");
        }

        if (order.Status == PurchaseOrderStatus.Received)
        {
            return (false, "Fully received purchase orders cannot be cancelled.");
        }

        if (order.Status == PurchaseOrderStatus.Cancelled)
        {
            return (false, "Purchase order is already cancelled.");
        }

        var now = DateTime.UtcNow;
        order.Status = PurchaseOrderStatus.Cancelled;
        order.CancelledAt = now;
        order.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Purchase order cancelled.");
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

    private async Task<(bool Succeeded, string Message)> AddOrUpdatePurchaseOrderLineCoreAsync(
        PurchaseOrder order,
        int productId,
        int orderedQuantity,
        CancellationToken cancellationToken)
    {
        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return (false, "Only draft purchase orders can be edited.");
        }

        if (orderedQuantity <= 0)
        {
            return (false, "Ordered quantity must be greater than zero.");
        }

        var product = await db.Products.FirstOrDefaultAsync(
            value => value.Id == productId,
            cancellationToken);

        if (product is null)
        {
            return (false, "Choose a valid product.");
        }

        if (product.SupplierId != order.SupplierId)
        {
            return (false, "Product must belong to the purchase order supplier.");
        }

        var existingLine = order.Lines.FirstOrDefault(value => value.ProductId == productId);
        if (existingLine is null)
        {
            order.Lines.Add(new PurchaseOrderLine
            {
                ProductId = productId,
                OrderedQuantity = orderedQuantity,
                ReceivedQuantity = 0
            });
        }
        else
        {
            existingLine.OrderedQuantity = orderedQuantity;
        }

        return (true, "Purchase order line saved.");
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<AiUsageImportRow> ParseAiUsageImportRows(
        string content,
        string? format,
        List<AiTokenUsageImportError> errors)
    {
        var normalizedFormat = NormalizeOptional(format)?.ToLowerInvariant();
        var trimmed = content.TrimStart();

        try
        {
            return normalizedFormat switch
            {
                null or "" when trimmed.StartsWith('[') || trimmed.StartsWith('{') => ParseAiUsageJsonRows(content, errors),
                null or "" => ParseAiUsageCsvRows(content, errors),
                "json" => ParseAiUsageJsonRows(content, errors),
                "csv" => ParseAiUsageCsvRows(content, errors),
                _ => AddImportParseError(errors, $"Unsupported AI usage import format '{format}'. Use csv or json.")
            };
        }
        catch (JsonException ex)
        {
            errors.Add(new AiTokenUsageImportError(0, $"JSON import could not be parsed: {ex.Message}"));
            return [];
        }
    }

    private static IReadOnlyList<AiUsageImportRow> ParseAiUsageJsonRows(
        string content,
        List<AiTokenUsageImportError> errors)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var rows = new List<AiUsageImportRow>();

        if (root.ValueKind == JsonValueKind.Object &&
            TryGetJsonProperty(root, out var data, "data", "records", "rows", "items", "usage"))
        {
            root = data;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            rows.Add(new AiUsageImportRow(1, JsonObjectToDictionary(root)));
            return rows;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new AiTokenUsageImportError(0, "JSON import must be an object, an array, or an object with a records/data array."));
            return [];
        }

        var rowNumber = 1;
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                rows.Add(new AiUsageImportRow(rowNumber, JsonObjectToDictionary(item)));
            }
            else
            {
                errors.Add(new AiTokenUsageImportError(rowNumber, "JSON array item must be an object."));
            }

            rowNumber++;
        }

        return rows;
    }

    private static bool TryGetJsonProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        var normalizedNames = names.Select(NormalizeImportKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (normalizedNames.Contains(NormalizeImportKey(property.Name)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Dictionary<string, string?> JsonObjectToDictionary(JsonElement element)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            values[NormalizeImportKey(property.Name)] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText()
            };
        }

        return values;
    }

    private static IReadOnlyList<AiUsageImportRow> ParseAiUsageCsvRows(
        string content,
        List<AiTokenUsageImportError> errors)
    {
        var records = SplitCsvRecords(content);
        if (records.Count == 0)
        {
            errors.Add(new AiTokenUsageImportError(0, "CSV import is empty."));
            return [];
        }

        var headers = records[0]
            .Select(NormalizeImportKey)
            .ToList();

        if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
        {
            errors.Add(new AiTokenUsageImportError(0, "CSV import must include a header row."));
            return [];
        }

        var rows = new List<AiUsageImportRow>();
        for (var index = 1; index < records.Count; index++)
        {
            var cells = records[index];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < headers.Count && column < cells.Count; column++)
            {
                if (!string.IsNullOrWhiteSpace(headers[column]) && !IsSecretImportKey(headers[column]))
                {
                    values[headers[column]] = cells[column];
                }
            }

            rows.Add(new AiUsageImportRow(index + 1, values));
        }

        return rows;
    }

    private static List<List<string>> SplitCsvRecords(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < content.Length && content[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                row.Add(current.ToString().Trim());
                current.Clear();
            }
            else if ((character == '\n' || character == '\r') && !inQuotes)
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(current.ToString().Trim());
                current.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                current.Append(character);
            }
        }

        if (inQuotes)
        {
            return rows;
        }

        row.Add(current.ToString().Trim());
        if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0]))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static bool TryCreateAiTokenUsageRecord(
        AiUsageImportRow row,
        List<AiTokenUsageImportError> errors,
        out AiTokenUsageRecord record)
    {
        var rowErrors = new List<string>();
        var provider = ReadString(row.Values, "provider", "directprovider") ?? "OpenAI";
        var billingProvider = ReadString(row.Values, "billingprovider", "billing", "gateway") ?? provider;
        var notes = ReadString(row.Values, "notes", "note");

        if (ContainsSecretLikeValue(notes))
        {
            rowErrors.Add("Notes look like they contain a secret and were not imported.");
        }

        record = new AiTokenUsageRecord
        {
            FeatureName = ReadString(row.Values, "featurename", "feature", "source", "app", "endpoint") ?? string.Empty,
            Provider = provider,
            BillingProvider = billingProvider,
            UpstreamProvider = ReadString(row.Values, "upstreamprovider", "upstream"),
            ModelName = ReadString(row.Values, "modelname", "model", "modelslug", "openroutermodelslug") ?? string.Empty,
            OpenRouterModelSlug = ReadString(row.Values, "openroutermodelslug", "modelpermaslug", "modelslug"),
            RouteName = ReadString(row.Values, "routename", "route", "router", "servicetier"),
            PromptTokens = ReadInt(row.Values, rowErrors, "prompttokens", "inputtokens", "tokensprompt", "nativetokensprompt"),
            CachedInputTokens = ReadInt(row.Values, rowErrors, "cachedinputtokens", "cachetokens", "cachedtokens", "nativetokenscached"),
            CompletionTokens = ReadInt(row.Values, rowErrors, "completiontokens", "outputtokens", "tokenscompletion", "nativetokenscompletion"),
            ReasoningTokens = ReadInt(row.Values, rowErrors, "reasoningtokens", "nativetokensreasoning"),
            ToolCallCount = ReadInt(row.Values, rowErrors, "toolcallcount", "toolcalls"),
            SearchCallCount = ReadInt(row.Values, rowErrors, "searchcallcount", "searchcalls", "numsearchresults"),
            PromptCostPerMillionTokens = ReadDecimal(row.Values, rowErrors, "promptcostpermilliontokens", "inputcostpermilliontokens"),
            CachedInputCostPerMillionTokens = ReadDecimal(row.Values, rowErrors, "cachedinputcostpermilliontokens"),
            CompletionCostPerMillionTokens = ReadDecimal(row.Values, rowErrors, "completioncostpermilliontokens", "outputcostpermilliontokens"),
            SearchCostPerThousandCalls = ReadDecimal(row.Values, rowErrors, "searchcostperthousandcalls"),
            ActualCostUsd = ReadNullableDecimal(row.Values, rowErrors, "actualcostusd", "costusd", "cost", "totalcost", "usage", "upstreaminferencecost"),
            CreatedAt = ReadDateTime(row.Values, rowErrors, "createdat", "created", "date", "timestamp") ?? DateTime.UtcNow,
            Notes = notes
        };

        foreach (var rowError in rowErrors)
        {
            errors.Add(new AiTokenUsageImportError(row.RowNumber, rowError));
        }

        return rowErrors.Count == 0;
    }

    private static string NormalizeImportKey(string key)
    {
        return new string(key
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static bool IsSecretImportKey(string key)
    {
        var normalized = NormalizeImportKey(key);
        return normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal) ||
            normalized is "key" or "token" or "accesstoken" or "refreshtoken";
    }

    private static bool ContainsSecretLikeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("authorization:", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("bearer ", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(Dictionary<string, string?> row, params string[] keys)
    {
        foreach (var key in keys.Select(NormalizeImportKey))
        {
            if (!IsSecretImportKey(key) &&
                row.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static int ReadInt(Dictionary<string, string?> row, List<string> errors, params string[] keys)
    {
        var value = ReadString(row, keys);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        errors.Add($"{keys[0]} must be a whole number.");
        return 0;
    }

    private static decimal ReadDecimal(Dictionary<string, string?> row, List<string> errors, params string[] keys)
    {
        return ReadNullableDecimal(row, errors, keys) ?? 0m;
    }

    private static decimal? ReadNullableDecimal(Dictionary<string, string?> row, List<string> errors, params string[] keys)
    {
        var value = ReadString(row, keys);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed;
        }

        errors.Add($"{keys[0]} must be a number.");
        return null;
    }

    private static DateTime? ReadDateTime(Dictionary<string, string?> row, List<string> errors, params string[] keys)
    {
        var value = ReadString(row, keys);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal, out parsed))
        {
            return parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
        }

        errors.Add($"{keys[0]} must be a date/time.");
        return null;
    }

    private static IReadOnlyList<AiUsageImportRow> AddImportParseError(
        List<AiTokenUsageImportError> errors,
        string message)
    {
        errors.Add(new AiTokenUsageImportError(0, message));
        return [];
    }

    private static DateTime ToMonthStart(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return new DateTime(utcValue.Year, utcValue.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static AiBudgetStatusDto ToBudgetStatus(AiMonthlyBudget budget, decimal effectiveSpendUsd)
    {
        var percentUsed = budget.BudgetUsd == 0
            ? effectiveSpendUsd > 0 ? 100m : 0m
            : Math.Round(effectiveSpendUsd / budget.BudgetUsd * 100m, 2, MidpointRounding.AwayFromZero);
        var isCritical = percentUsed >= budget.CriticalThresholdPercent;
        var isWarning = !isCritical && percentUsed >= budget.WarningThresholdPercent;
        var status = isCritical ? "Critical" : isWarning ? "Warning" : "Ok";

        return new AiBudgetStatusDto(
            budget.Id,
            budget.MonthStart,
            budget.BudgetUsd,
            budget.WarningThresholdPercent,
            budget.CriticalThresholdPercent,
            effectiveSpendUsd,
            budget.BudgetUsd - effectiveSpendUsd,
            percentUsed,
            isWarning,
            isCritical,
            status);
    }

    private static IReadOnlyList<AiUsageGroupDto> BuildUsageGroups(
        IReadOnlyList<AiTokenUsageRecord> records,
        string groupBy)
    {
        static string ProviderKey(AiTokenUsageRecord record) =>
            $"{record.BillingProvider} / {record.Provider} / {record.UpstreamProvider ?? "Direct"}";

        static string ModelKey(AiTokenUsageRecord record) =>
            string.IsNullOrWhiteSpace(record.OpenRouterModelSlug)
                ? record.ModelName
                : record.OpenRouterModelSlug;

        static string RouteKey(AiTokenUsageRecord record) =>
            string.IsNullOrWhiteSpace(record.RouteName)
                ? $"{ModelKey(record)} / Standard"
                : $"{ModelKey(record)} / {record.RouteName}";

        var groups = groupBy switch
        {
            "feature" => records.GroupBy(record => record.FeatureName),
            "provider" => records.GroupBy(ProviderKey),
            "model" => records.GroupBy(ModelKey),
            "route" => records.GroupBy(RouteKey),
            _ => records.GroupBy(record => record.FeatureName)
        };

        return groups
            .Select(group =>
            {
                var first = group.First();
                var estimated = group.Sum(record => record.EstimatedCostUsd);
                var actual = group.Sum(record => record.ActualCostUsd ?? 0m);
                var effective = group.Sum(record => record.EffectiveCostUsd);

                return new AiUsageGroupDto(
                    group.Key,
                    first.Provider,
                    first.BillingProvider,
                    first.UpstreamProvider,
                    first.ModelName,
                    first.OpenRouterModelSlug,
                    first.RouteName,
                    group.Count(),
                    group.Sum(record => record.TotalTokens),
                    group.Sum(record => record.SearchCallCount),
                    estimated,
                    actual,
                    effective);
            })
            .OrderByDescending(group => group.EffectiveCostUsd)
            .ThenBy(group => group.Key)
            .ToList();
    }

    private static decimal CalculateTokenCost(
        int promptTokens,
        int cachedInputTokens,
        int completionTokens,
        int searchCallCount,
        decimal promptCostPerMillionTokens,
        decimal cachedInputCostPerMillionTokens,
        decimal completionCostPerMillionTokens,
        decimal searchCostPerThousandCalls)
    {
        var inputCost = promptTokens / 1_000_000m * promptCostPerMillionTokens;
        var cachedInputCost = cachedInputTokens / 1_000_000m * cachedInputCostPerMillionTokens;
        var outputCost = completionTokens / 1_000_000m * completionCostPerMillionTokens;
        var searchCost = searchCallCount / 1_000m * searchCostPerThousandCalls;
        return Math.Round(inputCost + cachedInputCost + outputCost + searchCost, 6, MidpointRounding.AwayFromZero);
    }

    private static void ApplyPriceSnapshot(AiTokenUsageRecord record, AiModelPrice price)
    {
        record.Provider = price.Provider;
        record.BillingProvider = price.BillingProvider ?? price.Provider;
        record.UpstreamProvider = price.UpstreamProvider;
        record.ModelName = price.ModelName;
        record.OpenRouterModelSlug = price.OpenRouterModelSlug;
        record.RouteName = price.RouteName;
        record.PromptCostPerMillionTokens = price.InputCostPerMillionTokens;
        record.CachedInputCostPerMillionTokens = price.CachedInputCostPerMillionTokens;
        record.CompletionCostPerMillionTokens = price.OutputCostPerMillionTokens;
        record.SearchCostPerThousandCalls = price.SearchCostPerThousandCalls;
    }

    private sealed record AiUsageImportRow(
        int RowNumber,
        Dictionary<string, string?> Values);

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
