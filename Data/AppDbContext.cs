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

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();

    public DbSet<AiTokenUsageRecord> AiTokenUsageRecords => Set<AiTokenUsageRecord>();

    public DbSet<AiModelPrice> AiModelPrices => Set<AiModelPrice>();

    public DbSet<AiBudgetSettings> AiBudgetSettings => Set<AiBudgetSettings>();

    public DbSet<AiMonthlyBudget> AiMonthlyBudgets => Set<AiMonthlyBudget>();

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

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.Property(order => order.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(order => order.Supplier)
                .WithMany(supplier => supplier.PurchaseOrders)
                .HasForeignKey(order => order.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(order => order.Lines)
                .WithOne(line => line.PurchaseOrder)
                .HasForeignKey(line => line.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(order => order.CreatedAt);
            entity.HasIndex(order => order.Status);
        });

        modelBuilder.Entity<PurchaseOrderLine>(entity =>
        {
            entity.Property(line => line.OrderedQuantity)
                .IsRequired();

            entity.Property(line => line.ReceivedQuantity)
                .HasDefaultValue(0);

            entity.HasOne(line => line.Product)
                .WithMany(product => product.PurchaseOrderLines)
                .HasForeignKey(line => line.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(line => new { line.PurchaseOrderId, line.ProductId })
                .IsUnique();
        });

        modelBuilder.Entity<AiTokenUsageRecord>(entity =>
        {
            entity.Property(record => record.FeatureName)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(record => record.Provider)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(record => record.BillingProvider)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(record => record.UpstreamProvider)
                .HasMaxLength(80);

            entity.Property(record => record.ModelName)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(record => record.OpenRouterModelSlug)
                .HasMaxLength(160);

            entity.Property(record => record.RouteName)
                .HasMaxLength(120);

            entity.Property(record => record.PromptCostPerMillionTokens)
                .HasColumnType("decimal(18,6)");

            entity.Property(record => record.CachedInputCostPerMillionTokens)
                .HasColumnType("decimal(18,6)");

            entity.Property(record => record.CompletionCostPerMillionTokens)
                .HasColumnType("decimal(18,6)");

            entity.Property(record => record.EstimatedCostUsd)
                .HasColumnType("decimal(18,6)");

            entity.Property(record => record.ActualCostUsd)
                .HasColumnType("decimal(18,6)");

            entity.Property(record => record.SearchCostPerThousandCalls)
                .HasColumnType("decimal(18,6)");

            entity.Property(record => record.Notes)
                .HasMaxLength(500);

            entity.HasOne(record => record.AiModelPrice)
                .WithMany(price => price.UsageRecords)
                .HasForeignKey(record => record.AiModelPriceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(record => record.CreatedAt);
            entity.HasIndex(record => record.ModelName);
            entity.HasIndex(record => record.BillingProvider);
            entity.HasIndex(record => record.FeatureName);
        });

        modelBuilder.Entity<AiModelPrice>(entity =>
        {
            entity.Property(price => price.Provider)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(price => price.BillingProvider)
                .HasMaxLength(80);

            entity.Property(price => price.UpstreamProvider)
                .HasMaxLength(80);

            entity.Property(price => price.ModelName)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(price => price.OpenRouterModelSlug)
                .HasMaxLength(160);

            entity.Property(price => price.RouteName)
                .HasMaxLength(120);

            entity.Property(price => price.Currency)
                .IsRequired()
                .HasMaxLength(3);

            entity.Property(price => price.InputCostPerMillionTokens)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.CachedInputCostPerMillionTokens)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.OutputCostPerMillionTokens)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.SearchCostPerThousandCalls)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.SourceUrl)
                .HasMaxLength(500);

            entity.HasIndex(price => new { price.Provider, price.ModelName, price.RouteName, price.EffectiveFrom })
                .IsUnique();

            entity.HasIndex(price => price.IsDefault);
        });

        modelBuilder.Entity<AiMonthlyBudget>(entity =>
        {
            entity.Property(budget => budget.BudgetUsd)
                .HasColumnType("decimal(18,6)");

            entity.Property(budget => budget.WarningThresholdPercent)
                .HasColumnType("decimal(5,2)");

            entity.Property(budget => budget.CriticalThresholdPercent)
                .HasColumnType("decimal(5,2)");

            entity.HasIndex(budget => budget.MonthStart)
                .IsUnique();
        });

        modelBuilder.Entity<AiBudgetSettings>(entity =>
        {
            entity.Property(settings => settings.MonthlyBudgetUsd)
                .HasColumnType("decimal(18,6)");

            entity.Property(settings => settings.WarningThresholdPercent)
                .HasColumnType("decimal(5,2)");
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

            await CreateTableIfMissingAsync(
                connection,
                "PurchaseOrders",
                """
                CREATE TABLE IF NOT EXISTS PurchaseOrders (
                    Id INTEGER NOT NULL CONSTRAINT PK_PurchaseOrders PRIMARY KEY AUTOINCREMENT,
                    SupplierId INTEGER NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    OrderedAt TEXT NULL,
                    ReceivedAt TEXT NULL,
                    CancelledAt TEXT NULL,
                    CONSTRAINT FK_PurchaseOrders_Suppliers_SupplierId FOREIGN KEY (SupplierId) REFERENCES Suppliers (Id) ON DELETE RESTRICT
                );
                """,
                cancellationToken);

            await CreateTableIfMissingAsync(
                connection,
                "PurchaseOrderLines",
                """
                CREATE TABLE IF NOT EXISTS PurchaseOrderLines (
                    Id INTEGER NOT NULL CONSTRAINT PK_PurchaseOrderLines PRIMARY KEY AUTOINCREMENT,
                    PurchaseOrderId INTEGER NOT NULL,
                    ProductId INTEGER NOT NULL,
                    OrderedQuantity INTEGER NOT NULL,
                    ReceivedQuantity INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT FK_PurchaseOrderLines_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders (Id) ON DELETE CASCADE
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_CreatedAt ON PurchaseOrders (CreatedAt);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_Status ON PurchaseOrders (Status);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_SupplierId ON PurchaseOrders (SupplierId);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_PurchaseOrderLines_ProductId ON PurchaseOrderLines (ProductId);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_PurchaseOrderLines_PurchaseOrderId_ProductId ON PurchaseOrderLines (PurchaseOrderId, ProductId);",
                cancellationToken);

            await CreateTableIfMissingAsync(
                connection,
                "AiTokenUsageRecords",
                """
                CREATE TABLE IF NOT EXISTS AiTokenUsageRecords (
                    Id INTEGER NOT NULL CONSTRAINT PK_AiTokenUsageRecords PRIMARY KEY AUTOINCREMENT,
                    FeatureName TEXT NOT NULL,
                    Provider TEXT NOT NULL,
                    BillingProvider TEXT NOT NULL DEFAULT 'OpenAI',
                    UpstreamProvider TEXT NULL,
                    ModelName TEXT NOT NULL,
                    OpenRouterModelSlug TEXT NULL,
                    RouteName TEXT NULL,
                    PromptTokens INTEGER NOT NULL,
                    CompletionTokens INTEGER NOT NULL,
                    CachedInputTokens INTEGER NOT NULL,
                    PromptCostPerMillionTokens TEXT NOT NULL,
                    CompletionCostPerMillionTokens TEXT NOT NULL,
                    EstimatedCostUsd TEXT NOT NULL,
                    Notes TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """,
                cancellationToken);

            await CreateTableIfMissingAsync(
                connection,
                "AiModelPrices",
                """
                CREATE TABLE IF NOT EXISTS AiModelPrices (
                    Id INTEGER NOT NULL CONSTRAINT PK_AiModelPrices PRIMARY KEY AUTOINCREMENT,
                    Provider TEXT NOT NULL,
                    BillingProvider TEXT NULL,
                    UpstreamProvider TEXT NULL,
                    ModelName TEXT NOT NULL,
                    OpenRouterModelSlug TEXT NULL,
                    RouteName TEXT NULL,
                    Currency TEXT NOT NULL DEFAULT 'USD',
                    InputCostPerMillionTokens TEXT NOT NULL,
                    CachedInputCostPerMillionTokens TEXT NOT NULL DEFAULT '0',
                    OutputCostPerMillionTokens TEXT NOT NULL,
                    SearchCostPerThousandCalls TEXT NOT NULL DEFAULT '0',
                    EffectiveFrom TEXT NOT NULL,
                    EffectiveTo TEXT NULL,
                    IsDefault INTEGER NOT NULL DEFAULT 0,
                    IsCustom INTEGER NOT NULL DEFAULT 0,
                    SourceUrl TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """,
                cancellationToken);

            await CreateTableIfMissingAsync(
                connection,
                "AiMonthlyBudgets",
                """
                CREATE TABLE IF NOT EXISTS AiMonthlyBudgets (
                    Id INTEGER NOT NULL CONSTRAINT PK_AiMonthlyBudgets PRIMARY KEY AUTOINCREMENT,
                    MonthStart TEXT NOT NULL,
                    BudgetUsd TEXT NOT NULL,
                    WarningThresholdPercent TEXT NOT NULL DEFAULT '80',
                    CriticalThresholdPercent TEXT NOT NULL DEFAULT '100',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """,
                cancellationToken);

            await CreateTableIfMissingAsync(
                connection,
                "AiBudgetSettings",
                """
                CREATE TABLE IF NOT EXISTS AiBudgetSettings (
                    Id INTEGER NOT NULL CONSTRAINT PK_AiBudgetSettings PRIMARY KEY,
                    MonthlyBudgetUsd TEXT NOT NULL,
                    WarningThresholdPercent TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                "INSERT OR IGNORE INTO AiBudgetSettings (Id, MonthlyBudgetUsd, WarningThresholdPercent, UpdatedAt) VALUES (1, '25', '80', '2026-06-19 00:00:00');",
                cancellationToken);

            var aiUsageColumns = await GetColumnNamesAsync(connection, "AiTokenUsageRecords", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "AiModelPriceId", "INTEGER", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "BillingProvider", "TEXT NOT NULL DEFAULT 'OpenAI'", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "UpstreamProvider", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "OpenRouterModelSlug", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "RouteName", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "ReasoningTokens", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "ToolCallCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "SearchCallCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "CachedInputCostPerMillionTokens", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "ActualCostUsd", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiUsageColumns, "AiTokenUsageRecords", "SearchCostPerThousandCalls", "TEXT NOT NULL DEFAULT '0'", cancellationToken);

            var aiPriceColumns = await GetColumnNamesAsync(connection, "AiModelPrices", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiPriceColumns, "AiModelPrices", "BillingProvider", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiPriceColumns, "AiModelPrices", "UpstreamProvider", "TEXT", cancellationToken);
            await AddColumnIfMissingAsync(connection, aiPriceColumns, "AiModelPrices", "OpenRouterModelSlug", "TEXT", cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_AiTokenUsageRecords_CreatedAt ON AiTokenUsageRecords (CreatedAt);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_AiTokenUsageRecords_ModelName ON AiTokenUsageRecords (ModelName);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_AiTokenUsageRecords_AiModelPriceId ON AiTokenUsageRecords (AiModelPriceId);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_AiTokenUsageRecords_BillingProvider ON AiTokenUsageRecords (BillingProvider);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_AiTokenUsageRecords_FeatureName ON AiTokenUsageRecords (FeatureName);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_AiModelPrices_Provider_ModelName_RouteName_EffectiveFrom ON AiModelPrices (Provider, ModelName, RouteName, EffectiveFrom);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_AiModelPrices_IsDefault ON AiModelPrices (IsDefault);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_AiMonthlyBudgets_MonthStart ON AiMonthlyBudgets (MonthStart);",
                cancellationToken);
            await SeedDefaultAiModelPricesAsync(connection, cancellationToken);
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

    private static Task CreateTableIfMissingAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        string createTableCommand,
        CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync(connection, createTableCommand, cancellationToken);
    }

    private static Task SeedDefaultAiModelPricesAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        const string now = "2026-06-19 00:00:00";
        const string openAiSource = "https://openai.com/api/pricing/";
        const string anthropicSource = "https://platform.claude.com/docs/en/about-claude/pricing";
        const string googleSource = "https://ai.google.dev/gemini-api/docs/pricing";
        const string cohereSource = "https://cohere.com/pricing";

        var rows = new[]
        {
            SqlPrice("OpenAI", "GPT-5.5", "Standard", 5.00m, 0.50m, 30.00m, 0m, openAiSource, now),
            SqlPrice("OpenAI", "GPT-5.4", "Standard", 2.50m, 0.25m, 15.00m, 0m, openAiSource, now),
            SqlPrice("OpenAI", "GPT-5.4 mini", "Standard", 0.75m, 0.075m, 4.50m, 0m, openAiSource, now),
            SqlPrice("Anthropic", "Claude Sonnet 4.6", "Standard", 3.00m, 0.30m, 15.00m, 0m, anthropicSource, now),
            SqlPrice("Anthropic", "Claude Sonnet 4.5", "Standard", 3.00m, 0.30m, 15.00m, 0m, anthropicSource, now),
            SqlPrice("Anthropic", "Claude Haiku 4.5", "Standard", 1.00m, 0.10m, 5.00m, 0m, anthropicSource, now),
            SqlPrice("Anthropic", "Claude Opus 4.7", "Standard", 5.00m, 0.50m, 25.00m, 0m, anthropicSource, now),
            SqlPrice("Google", "Gemini 2.5 Pro", "<=200k prompt", 1.25m, 0.125m, 10.00m, 35.00m, googleSource, now),
            SqlPrice("Google", "Gemini 2.5 Pro", ">200k prompt", 2.50m, 0.25m, 15.00m, 35.00m, googleSource, now),
            SqlPrice("Google", "Gemini 2.5 Flash", "Standard text/image/video", 0.30m, 0.03m, 2.50m, 35.00m, googleSource, now),
            SqlPrice("Cohere", "Command R+ 08-2024", "Legacy", 2.50m, 0m, 10.00m, 0m, cohereSource, now),
            SqlPrice("Cohere", "Command R 03-2024", "Legacy", 0.50m, 0m, 1.50m, 0m, cohereSource, now),
            SqlPrice("Cohere", "Aya Expanse API", "Standard", 0.50m, 0m, 1.50m, 0m, cohereSource, now)
        };

        return ExecuteNonQueryAsync(
            connection,
            string.Join(Environment.NewLine, rows),
            cancellationToken);
    }

    private static string SqlPrice(
        string provider,
        string modelName,
        string routeName,
        decimal input,
        decimal cached,
        decimal output,
        decimal search,
        string sourceUrl,
        string timestamp)
    {
        static string Value(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return $"""
            INSERT OR IGNORE INTO AiModelPrices
                (Provider, ModelName, RouteName, Currency, InputCostPerMillionTokens, CachedInputCostPerMillionTokens, OutputCostPerMillionTokens, SearchCostPerThousandCalls, EffectiveFrom, IsDefault, IsCustom, SourceUrl, CreatedAt, UpdatedAt)
            VALUES
                ('{provider.Replace("'", "''")}', '{modelName.Replace("'", "''")}', '{routeName.Replace("'", "''")}', 'USD', '{Value(input)}', '{Value(cached)}', '{Value(output)}', '{Value(search)}', '{timestamp}', 1, 0, '{sourceUrl.Replace("'", "''")}', '{timestamp}', '{timestamp}');
            """;
    }
}
