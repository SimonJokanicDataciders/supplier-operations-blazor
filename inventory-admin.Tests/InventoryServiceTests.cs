using inventory_admin.Data;
using inventory_admin.Dtos;
using inventory_admin.Models;
using inventory_admin.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task StockOutAsync_DoesNotAllowNegativeStock()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 3);

        var result = await fixture.Service.StockOutAsync(
            product.Id,
            quantity: 5,
            reason: "Too much usage");

        var savedProduct = await fixture.Db.Products.SingleAsync(item => item.Id == product.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("Stock out cannot make current stock negative.", result.Message);
        Assert.Equal(3, savedProduct.CurrentStock);
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task AdjustStockAsync_SetsFinalStockAndRecordsDifference()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 12);

        var result = await fixture.Service.AdjustStockAsync(
            product.Id,
            countedStock: 9,
            reason: "Cycle count");

        var savedProduct = await fixture.Db.Products.SingleAsync(item => item.Id == product.Id);
        var movement = await fixture.Db.StockMovements.SingleAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(9, savedProduct.CurrentStock);
        Assert.Equal(StockMovementType.StockAdjustment, movement.MovementType);
        Assert.Equal(3, movement.Quantity);
        Assert.Equal("Cycle count", movement.Reason);
    }

    [Fact]
    public async Task AdjustStockAsync_SameCountCreatesNoMovement()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 12);

        var result = await fixture.Service.AdjustStockAsync(
            product.Id,
            countedStock: 12,
            reason: "Cycle count");

        var savedProduct = await fixture.Db.Products.SingleAsync(item => item.Id == product.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("No adjustment needed. Counted stock already matches current stock.", result.Message);
        Assert.Equal(12, savedProduct.CurrentStock);
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ImportSuppliersAsync_UpdatesExistingSupplierByExternalKey()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        fixture.Db.Suppliers.Add(new Supplier
        {
            Name = "Old Supplier Name",
            CountryCode = "AT",
            SourceSystem = "SupplierIntelligence",
            ExternalSupplierKey = "supplier-123",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.ImportSuppliersAsync(new SupplierImportRequest(
            [
                new SupplierImportItemDto(
                    "Updated Supplier",
                    "DE",
                    "ops@example.com",
                    "https://supplier.example",
                    "Bearings",
                    "Imported from research",
                    "SupplierIntelligence",
                    "supplier-123",
                    "https://research.example")
            ]));

        var suppliers = await fixture.Db.Suppliers.ToListAsync();
        var supplier = Assert.Single(suppliers);

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal("Updated Supplier", supplier.Name);
        Assert.Equal("DE", supplier.CountryCode);
        Assert.Equal("ops@example.com", supplier.ContactEmail);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_StoresSupplierAndLines()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);

        var result = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);

        var order = await fixture.Db.PurchaseOrders
            .Include(value => value.Lines)
            .SingleAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(product.SupplierId, order.SupplierId);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        var line = Assert.Single(order.Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(8, line.OrderedQuantity);
        Assert.Equal(0, line.ReceivedQuantity);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_RejectsZeroQuantity()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);

        var result = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 0)]);

        Assert.False(result.Succeeded);
        Assert.Equal("Ordered quantity must be greater than zero.", result.Message);
        Assert.Empty(await fixture.Db.PurchaseOrders.ToListAsync());
    }

    [Fact]
    public async Task MarkPurchaseOrderOrderedAsync_MovesDraftToOrdered()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);

        var result = await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);

        var order = await fixture.Db.PurchaseOrders.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(PurchaseOrderStatus.Ordered, order.Status);
        Assert.NotNull(order.OrderedAt);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_DoesNotAllowMoreThanOpenQuantity()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 9)]);

        var savedProduct = await fixture.Db.Products.SingleAsync(value => value.Id == product.Id);
        Assert.False(result.Succeeded);
        Assert.Equal("Received quantity cannot be greater than the open ordered quantity.", result.Message);
        Assert.Equal(2, savedProduct.CurrentStock);
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_PartialReceiveUpdatesStatus()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 3)]);

        var order = await fixture.Db.PurchaseOrders.SingleAsync();
        var savedLine = await fixture.Db.PurchaseOrderLines.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
        Assert.Equal(3, savedLine.ReceivedQuantity);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_FullReceiveUpdatesStatus()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 8)]);

        var order = await fixture.Db.PurchaseOrders.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.NotNull(order.ReceivedAt);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_CreatesStockInMovementAndIncreasesStock()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 5)]);

        var savedProduct = await fixture.Db.Products.SingleAsync(value => value.Id == product.Id);
        var movement = await fixture.Db.StockMovements.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(7, savedProduct.CurrentStock);
        Assert.Equal(StockMovementType.StockIn, movement.MovementType);
        Assert.Equal(5, movement.Quantity);
        Assert.Equal($"Purchase order #{create.Order.Id} received", movement.Reason);
    }

    [Fact]
    public async Task CancelledPurchaseOrder_CannotBeReceived()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 2);
        var create = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, 8)]);
        await fixture.Service.MarkPurchaseOrderOrderedAsync(create.Order!.Id);
        await fixture.Service.CancelPurchaseOrderAsync(create.Order.Id);
        var line = await fixture.Db.PurchaseOrderLines.SingleAsync();

        var result = await fixture.Service.ReceivePurchaseOrderAsync(
            create.Order.Id,
            [new PurchaseOrderReceiveLineInput(line.Id, 1)]);

        Assert.False(result.Succeeded);
        Assert.Equal("Cancelled or fully received purchase orders cannot receive stock.", result.Message);
    }

    [Fact]
    public async Task LowStockPurchaseOrderCreateFlow_StoresSelectedSupplierAndProduct()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var product = await fixture.CreateProductAsync(currentStock: 1, reorderLevel: 6);
        var suggestedQuantity = Math.Max(product.ReorderLevel - product.CurrentStock, 1);

        var result = await fixture.Service.CreatePurchaseOrderAsync(
            product.SupplierId,
            [new PurchaseOrderLineInput(product.Id, suggestedQuantity)]);

        var order = await fixture.Db.PurchaseOrders
            .Include(value => value.Lines)
            .SingleAsync();
        var line = Assert.Single(order.Lines);

        Assert.True(result.Succeeded);
        Assert.Equal(product.SupplierId, order.SupplierId);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(5, line.OrderedQuantity);
    }

    [Fact]
    public async Task SaveAiTokenUsageRecordAsync_CalculatesEstimatedCost()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();

        var result = await fixture.Service.SaveAiTokenUsageRecordAsync(new AiTokenUsageRecord
        {
            FeatureName = "Supplier analysis",
            Provider = "OpenAI",
            ModelName = "gpt-test",
            PromptTokens = 1_000_000,
            CachedInputTokens = 500_000,
            CompletionTokens = 500_000,
            SearchCallCount = 2,
            PromptCostPerMillionTokens = 0.25m,
            CachedInputCostPerMillionTokens = 0.05m,
            CompletionCostPerMillionTokens = 2.00m,
            SearchCostPerThousandCalls = 10.00m
        });

        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1.295m, record.EstimatedCostUsd);
        Assert.Equal(2_000_000, record.TotalTokens);
    }

    [Fact]
    public async Task SaveAiTokenUsageRecordAsync_UsesSelectedModelPriceSnapshot()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var price = await fixture.Db.AiModelPrices
            .FirstAsync(value => value.Provider == "OpenAI" && value.ModelName == "GPT-5.4 mini");

        var result = await fixture.Service.SaveAiTokenUsageRecordAsync(new AiTokenUsageRecord
        {
            AiModelPriceId = price.Id,
            FeatureName = "Supplier analysis",
            Provider = "Ignored",
            ModelName = "Ignored",
            PromptTokens = 1_000_000,
            CachedInputTokens = 1_000_000,
            CompletionTokens = 1_000_000
        });

        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("OpenAI", record.Provider);
        Assert.Equal("GPT-5.4 mini", record.ModelName);
        Assert.Equal(0.75m, record.PromptCostPerMillionTokens);
        Assert.Equal(0.075m, record.CachedInputCostPerMillionTokens);
        Assert.Equal(4.50m, record.CompletionCostPerMillionTokens);
        Assert.Equal(5.325m, record.EstimatedCostUsd);
    }

    [Fact]
    public async Task SaveAiTokenUsageRecordAsync_SnapshotsOpenRouterFieldsFromSelectedPrice()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var priceResult = await fixture.Service.SaveAiModelPriceAsync(new AiModelPrice
        {
            Provider = "OpenRouter",
            BillingProvider = "OpenRouter",
            UpstreamProvider = "Anthropic",
            ModelName = "Claude Sonnet 4.5",
            OpenRouterModelSlug = "anthropic/claude-sonnet-4.5",
            RouteName = "floor-price",
            InputCostPerMillionTokens = 3m,
            CachedInputCostPerMillionTokens = 0.3m,
            OutputCostPerMillionTokens = 15m
        });

        var result = await fixture.Service.SaveAiTokenUsageRecordAsync(new AiTokenUsageRecord
        {
            AiModelPriceId = priceResult.Price!.Id,
            FeatureName = "Supplier analysis",
            Provider = "Ignored",
            BillingProvider = "Ignored",
            ModelName = "Ignored",
            PromptTokens = 1000,
            CompletionTokens = 1000
        });

        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("OpenRouter", record.Provider);
        Assert.Equal("OpenRouter", record.BillingProvider);
        Assert.Equal("Anthropic", record.UpstreamProvider);
        Assert.Equal("anthropic/claude-sonnet-4.5", record.OpenRouterModelSlug);
        Assert.Equal("floor-price", record.RouteName);
    }

    [Fact]
    public async Task ImportAiTokenUsageAsync_ImportsCsvWithSimpleColumns()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var csv = """
            feature,provider,model,input_tokens,output_tokens,cached_input_tokens,reasoning_tokens,search_calls,actual_cost_usd,created_at,notes
            Inventory assistant,OpenAI,gpt-test,100,50,25,10,2,0.123456,2026-06-01T12:30:00Z,First import
            """;

        var result = await fixture.Service.ImportAiTokenUsageAsync(new AiTokenUsageImportRequest(csv, "csv"));

        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();
        Assert.Equal(1, result.ImportedCount);
        Assert.Empty(result.Errors);
        Assert.Equal("Inventory assistant", record.FeatureName);
        Assert.Equal("OpenAI", record.Provider);
        Assert.Equal("OpenAI", record.BillingProvider);
        Assert.Equal("gpt-test", record.ModelName);
        Assert.Equal(100, record.PromptTokens);
        Assert.Equal(25, record.CachedInputTokens);
        Assert.Equal(50, record.CompletionTokens);
        Assert.Equal(10, record.ReasoningTokens);
        Assert.Equal(2, record.SearchCallCount);
        Assert.Equal(0.123456m, record.ActualCostUsd);
        Assert.Equal(new DateTime(2026, 6, 1, 12, 30, 0, DateTimeKind.Utc), record.CreatedAt);
        Assert.Equal("First import", record.Notes);
    }

    [Fact]
    public async Task ImportAiTokenUsageAsync_ImportsJsonWithCasingAndUnderscoreVariants()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var json = """
            {
              "records": [
                {
                  "Feature_Name": "Supplier research",
                  "Billing_Provider": "OpenRouter",
                  "Provider": "OpenRouter",
                  "UpstreamProvider": "Anthropic",
                  "Model": "Claude Sonnet",
                  "openrouter_model_slug": "anthropic/claude-sonnet",
                  "route": "balanced",
                  "InputTokens": 200,
                  "Output_Tokens": 75,
                  "ToolCalls": 3,
                  "Search_Calls": 1,
                  "Actual_Cost_USD": 0.42
                }
              ]
            }
            """;

        var result = await fixture.Service.ImportAiTokenUsageAsync(new AiTokenUsageImportRequest(json, null));

        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();
        Assert.Equal(1, result.ImportedCount);
        Assert.Empty(result.Errors);
        Assert.Equal("Supplier research", record.FeatureName);
        Assert.Equal("OpenRouter", record.BillingProvider);
        Assert.Equal("Anthropic", record.UpstreamProvider);
        Assert.Equal("Claude Sonnet", record.ModelName);
        Assert.Equal("anthropic/claude-sonnet", record.OpenRouterModelSlug);
        Assert.Equal("balanced", record.RouteName);
        Assert.Equal(200, record.PromptTokens);
        Assert.Equal(75, record.CompletionTokens);
        Assert.Equal(3, record.ToolCallCount);
        Assert.Equal(1, record.SearchCallCount);
        Assert.Equal(0.42m, record.ActualCostUsd);
    }

    [Fact]
    public async Task ImportAiTokenUsageAsync_ReturnsPerRowErrorsAndDoesNotStoreSecrets()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var csv = """
            feature,provider,model,input_tokens,output_tokens,actual_cost_usd,notes,api_key
            Good row,OpenAI,gpt-test,100,25,,ok,sk-ignore-me
            Bad number,OpenAI,gpt-test,nope,25,,ok,
            Secret notes,OpenAI,gpt-test,10,5,,api_key=sk-should-not-store,
            """;

        var result = await fixture.Service.ImportAiTokenUsageAsync(new AiTokenUsageImportRequest(csv, "csv"));

        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.RowNumber == 3 && error.Message.Contains("whole number", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.RowNumber == 4 && error.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Good row", record.FeatureName);
        Assert.Equal("ok", record.Notes);
        Assert.DoesNotContain("sk-", record.Notes ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAiUsageSummaryAsync_ReturnsBudgetStatusAndGroupedCosts()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        await fixture.Service.SaveAiMonthlyBudgetAsync(new AiMonthlyBudgetRequest(
            monthStart,
            BudgetUsd: 0.05m,
            WarningThresholdPercent: 50m,
            CriticalThresholdPercent: 90m));

        await fixture.Service.SaveAiTokenUsageRecordAsync(new AiTokenUsageRecord
        {
            FeatureName = "Supplier analysis",
            Provider = "OpenRouter",
            BillingProvider = "OpenRouter",
            UpstreamProvider = "OpenAI",
            ModelName = "gpt-test",
            OpenRouterModelSlug = "openai/gpt-test",
            RouteName = "balanced",
            PromptTokens = 10_000,
            CompletionTokens = 10_000,
            PromptCostPerMillionTokens = 1m,
            CompletionCostPerMillionTokens = 2m,
            ActualCostUsd = 0.04m
        });

        await fixture.Service.SaveAiTokenUsageRecordAsync(new AiTokenUsageRecord
        {
            FeatureName = "Supplier analysis",
            Provider = "OpenRouter",
            BillingProvider = "OpenRouter",
            UpstreamProvider = "OpenAI",
            ModelName = "gpt-test",
            OpenRouterModelSlug = "openai/gpt-test",
            RouteName = "balanced",
            PromptTokens = 5_000,
            CompletionTokens = 5_000,
            PromptCostPerMillionTokens = 1m,
            CompletionCostPerMillionTokens = 2m,
            ActualCostUsd = 0.02m
        });

        var summary = await fixture.Service.GetAiUsageSummaryAsync(monthStart);
        var featureGroup = Assert.Single(summary.ByFeature);
        var routeGroup = Assert.Single(summary.ByRoute);

        Assert.Equal(2, summary.RequestCount);
        Assert.Equal(30_000, summary.TotalTokens);
        Assert.Equal(0.045m, summary.EstimatedCostUsd);
        Assert.Equal(0.06m, summary.ActualCostUsd);
        Assert.Equal(0.06m, summary.EffectiveCostUsd);
        Assert.Equal("Critical", summary.Budget!.Status);
        Assert.Equal(120m, summary.Budget.PercentUsed);
        Assert.Equal("Supplier analysis", featureGroup.Key);
        Assert.Equal(0.06m, featureGroup.EffectiveCostUsd);
        Assert.Equal("openai/gpt-test / balanced", routeGroup.Key);
    }

    [Fact]
    public async Task SaveAiTokenUsageRecordAsync_RejectsEmptyTokenRun()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();

        var result = await fixture.Service.SaveAiTokenUsageRecordAsync(new AiTokenUsageRecord
        {
            FeatureName = "Supplier analysis",
            Provider = "OpenAI",
            ModelName = "gpt-test",
            PromptTokens = 0,
            CompletionTokens = 0,
            PromptCostPerMillionTokens = 0.25m,
            CompletionCostPerMillionTokens = 2.00m
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Enter at least one token, billable search call, or actual cost.", result.Message);
        Assert.Empty(await fixture.Db.AiTokenUsageRecords.ToListAsync());
    }

    [Fact]
    public async Task ImportAiTokenUsageAsync_ImportsCsvRowsWithCommonColumns()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        const string csv = """
            feature,provider,model,input_tokens,output_tokens,actual_cost_usd,created_at,notes
            Supplier assistant,OpenAI,gpt-test,1000,500,0.012,2026-06-02,first run
            """;

        var result = await fixture.Service.ImportAiTokenUsageAsync(new AiTokenUsageImportRequest(csv, "csv"));
        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Empty(result.Errors);
        Assert.Equal("Supplier assistant", record.FeatureName);
        Assert.Equal("OpenAI", record.Provider);
        Assert.Equal("gpt-test", record.ModelName);
        Assert.Equal(1000, record.PromptTokens);
        Assert.Equal(500, record.CompletionTokens);
        Assert.Equal(0.012m, record.ActualCostUsd);
        Assert.Equal("first run", record.Notes);
        Assert.Equal(2026, record.CreatedAt.Year);
    }

    [Fact]
    public async Task ImportAiTokenUsageAsync_ImportsOpenRouterJsonDataRows()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        const string json = """
            {
              "data": [
                {
                  "model": "openai/gpt-4.1",
                  "model_permaslug": "openai/gpt-4.1-2025-04-14",
                  "provider_name": "OpenAI",
                  "prompt_tokens": 50,
                  "completion_tokens": 125,
                  "reasoning_tokens": 25,
                  "usage": 0.015,
                  "date": "2026-06-03"
                }
              ]
            }
            """;

        var result = await fixture.Service.ImportAiTokenUsageAsync(new AiTokenUsageImportRequest(json, "json"));
        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Empty(result.Errors);
        Assert.Equal("Imported usage", record.FeatureName);
        Assert.Equal("OpenAI", record.Provider);
        Assert.Equal("OpenAI", record.UpstreamProvider);
        Assert.Equal("openai/gpt-4.1", record.ModelName);
        Assert.Equal("openai/gpt-4.1-2025-04-14", record.OpenRouterModelSlug);
        Assert.Equal(50, record.PromptTokens);
        Assert.Equal(125, record.CompletionTokens);
        Assert.Equal(25, record.ReasoningTokens);
        Assert.Equal(0.015m, record.ActualCostUsd);
    }

    private sealed class InventoryServiceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private InventoryServiceFixture(SqliteConnection connection, AppDbContext db)
        {
            this.connection = connection;
            Db = db;
            Service = new InventoryService(db);
        }

        public AppDbContext Db { get; }

        public InventoryService Service { get; }

        public static async Task<InventoryServiceFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            await db.EnsureAdditiveSchemaAsync();

            return new InventoryServiceFixture(connection, db);
        }

        public async Task<Product> CreateProductAsync(int currentStock, int reorderLevel = 2)
        {
            var category = new Category
            {
                Name = "Hardware"
            };
            var supplier = new Supplier
            {
                Name = "Test Supplier",
                CountryCode = "AT"
            };
            var product = new Product
            {
                Name = "Test Product",
                Sku = Guid.NewGuid().ToString("N")[..12],
                Category = category,
                Supplier = supplier,
                CurrentStock = currentStock,
                ReorderLevel = reorderLevel,
                UnitPrice = 10
            };

            Db.Products.Add(product);
            await Db.SaveChangesAsync();

            return product;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
