using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Services;

public partial class InventoryService
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
}
