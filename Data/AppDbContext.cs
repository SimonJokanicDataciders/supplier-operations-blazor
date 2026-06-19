using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace inventory_admin.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(category => category.Name)
                .IsRequired()
                .HasMaxLength(120);

            entity.HasIndex(category => category.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.Property(supplier => supplier.Name)
                .IsRequired()
                .HasMaxLength(160);

            entity.Property(supplier => supplier.CountryCode)
                .IsRequired()
                .HasMaxLength(2);

            entity.Property(supplier => supplier.Industry)
                .HasMaxLength(120);

            entity.Property(supplier => supplier.OperationalNotes)
                .HasMaxLength(2000);

            entity.Property(supplier => supplier.SourceSystem)
                .HasMaxLength(80);

            entity.Property(supplier => supplier.ExternalSupplierKey)
                .HasMaxLength(120);

            entity.Property(supplier => supplier.ResearchUrl)
                .HasMaxLength(500);

            entity.HasIndex(supplier => supplier.Name);

            entity.HasIndex(supplier => new { supplier.SourceSystem, supplier.ExternalSupplierKey });
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(product => product.Name)
                .IsRequired()
                .HasMaxLength(180);

            entity.Property(product => product.Sku)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(product => product.CurrentStock)
                .HasDefaultValue(0);

            entity.Property(product => product.ReorderLevel)
                .HasDefaultValue(0);

            entity.Property(product => product.UnitPrice)
                .HasColumnType("decimal(18,2)")
                .HasDefaultValue(0m);

            entity.HasIndex(product => product.Sku)
                .IsUnique();

            entity.HasOne(product => product.Category)
                .WithMany(category => category.Products)
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(product => product.Supplier)
                .WithMany(supplier => supplier.Products)
                .HasForeignKey(product => product.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.Property(movement => movement.MovementType)
                .HasConversion<string>()
                .HasMaxLength(24);

            entity.Property(movement => movement.Quantity)
                .IsRequired();

            entity.Property(movement => movement.Reason)
                .IsRequired()
                .HasMaxLength(500);

            entity.HasOne(movement => movement.Product)
                .WithMany(product => product.StockMovements)
                .HasForeignKey(movement => movement.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(movement => movement.CreatedAt);
        });
    }

    public async Task EnsureAdditiveSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!Database.IsSqlite())
        {
            return;
        }

        var connection = Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var existingColumns = await GetColumnNamesAsync(connection, "Suppliers", cancellationToken);

            await AddColumnIfMissingAsync(connection, existingColumns, "Suppliers", "Industry", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, existingColumns, "Suppliers", "OperationalNotes", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, existingColumns, "Suppliers", "SourceSystem", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, existingColumns, "Suppliers", "ExternalSupplierKey", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, existingColumns, "Suppliers", "ResearchUrl", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, existingColumns, "Suppliers", "ImportedAt", "TEXT", cancellationToken);
            var updatedAtAdded = await AddColumnIfMissingAsync(
                connection,
                existingColumns,
                "Suppliers",
                "UpdatedAt",
                "TEXT NOT NULL DEFAULT '1970-01-01 00:00:00'",
                cancellationToken);

            if (updatedAtAdded)
            {
                await ExecuteNonQueryAsync(
                    connection,
                    "UPDATE Suppliers SET UpdatedAt = CreatedAt WHERE UpdatedAt = '1970-01-01 00:00:00';",
                    cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<bool> AddColumnIfMissingAsync(
        System.Data.Common.DbConnection connection,
        HashSet<string> existingColumns,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (!existingColumns.Add(columnName))
        {
            return false;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static async Task ExecuteNonQueryAsync(
        System.Data.Common.DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
