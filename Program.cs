using inventory_admin.Components;
using inventory_admin.Data;
using inventory_admin.Dtos;
using inventory_admin.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("InventoryDb") ?? "Data Source=inventory-admin.db"));
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.MapOpenApi();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.EnsureAdditiveSchemaAsync();
    await InventoryDataSeeder.SeedAsync(db);
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/dashboard", async (DashboardService dashboard) =>
{
    var summary = await dashboard.GetSummaryAsync();
    return Results.Ok(new
    {
        summary.ProductCount,
        summary.SupplierCount,
        summary.LowStockCount,
        summary.SuppliersWithLowStock,
        summary.InventoryValue,
        summary.StockInQuantityLast30Days,
        summary.StockOutQuantityLast30Days,
        summary.AdjustmentCountLast30Days,
        summary.AdjustmentQuantityLast30Days,
        summary.SupplierOverview,
        summary.AiUsage,
        LowStockProducts = summary.LowStockProducts.Select(ToProductResponse),
        RecentMovements = summary.RecentMovements.Select(ToMovementResponse)
    });
})
.WithName("GetDashboardSummary");

app.MapGet("/api/products", async (InventoryService inventory) =>
{
    var products = await inventory.GetProductsAsync();
    return Results.Ok(products.Select(ToProductResponse));
})
.WithName("GetProducts");

app.MapGet("/api/products/low-stock", async (DashboardService dashboard) =>
{
    var summary = await dashboard.GetSummaryAsync();
    return Results.Ok(summary.LowStockProducts.Select(ToProductResponse));
})
.WithName("GetLowStockProducts");

app.MapGet("/api/suppliers", async (InventoryService inventory) =>
{
    var suppliers = await inventory.GetSupplierListAsync();
    return Results.Ok(suppliers);
})
.WithName("GetSuppliersApi");

app.MapGet("/api/suppliers/{id:int}", async (int id, InventoryService inventory) =>
{
    var supplier = await inventory.GetSupplierDetailDtoAsync(id);
    return supplier is null ? Results.NotFound() : Results.Ok(supplier);
})
.WithName("GetSupplierApi");

app.MapPost("/api/suppliers/import/preview", async (SupplierImportRequest request, InventoryService inventory) =>
{
    var preview = await inventory.PreviewSupplierImportAsync(request);
    return Results.Ok(preview);
})
.WithName("PreviewSupplierImport");

app.MapPost("/api/suppliers/import", async (SupplierImportRequest request, InventoryService inventory) =>
{
    var result = await inventory.ImportSuppliersAsync(request);
    return Results.Ok(result);
})
.WithName("ImportSuppliers");

app.MapGet("/api/suppliers/{id:int}/operations-summary", async (int id, InventoryService inventory) =>
{
    var summary = await inventory.GetSupplierOperationSummaryAsync(id);
    return summary is null ? Results.NotFound() : Results.Ok(summary);
})
.WithName("GetSupplierOperationsSummary");

app.MapGet("/api/stock-movements/summary", async (InventoryService inventory) =>
{
    var summary = await inventory.GetStockMovementSummaryAsync();
    return Results.Ok(summary);
})
.WithName("GetStockMovementSummary");

app.MapGet("/api/purchase-orders", async (InventoryService inventory) =>
{
    var orders = await inventory.GetPurchaseOrdersAsync();
    return Results.Ok(orders.Select(ToPurchaseOrderResponse));
})
.WithName("GetPurchaseOrders");

app.MapGet("/api/purchase-orders/{id:int}", async (int id, InventoryService inventory) =>
{
    var order = await inventory.GetPurchaseOrderAsync(id);
    return order is null ? Results.NotFound() : Results.Ok(ToPurchaseOrderResponse(order));
})
.WithName("GetPurchaseOrder");

app.MapPost("/api/purchase-orders", async (PurchaseOrderCreateRequest request, InventoryService inventory) =>
{
    var result = await inventory.CreatePurchaseOrderAsync(request.SupplierId, request.Lines);
    if (!result.Succeeded || result.Order is null)
    {
        return Results.BadRequest(new { result.Message });
    }

    var order = await inventory.GetPurchaseOrderAsync(result.Order.Id);
    return Results.Ok(ToPurchaseOrderResponse(order!));
})
.WithName("CreatePurchaseOrder");

app.MapPost("/api/purchase-orders/{id:int}/order", async (int id, InventoryService inventory) =>
{
    var result = await inventory.MarkPurchaseOrderOrderedAsync(id);
    return result.Succeeded
        ? Results.Ok(new { result.Message })
        : Results.BadRequest(new { result.Message });
})
.WithName("MarkPurchaseOrderOrdered");

app.MapPost("/api/purchase-orders/{id:int}/receive", async (
    int id,
    PurchaseOrderReceiveRequest request,
    InventoryService inventory) =>
{
    var result = await inventory.ReceivePurchaseOrderAsync(id, request.Lines ?? []);
    return result.Succeeded
        ? Results.Ok(new { result.Message })
        : Results.BadRequest(new { result.Message });
})
.WithName("ReceivePurchaseOrder");

app.MapPost("/api/purchase-orders/{id:int}/cancel", async (int id, InventoryService inventory) =>
{
    var result = await inventory.CancelPurchaseOrderAsync(id);
    return result.Succeeded
        ? Results.Ok(new { result.Message })
        : Results.BadRequest(new { result.Message });
})
.WithName("CancelPurchaseOrder");

app.MapPost("/api/stock-movements/adjust", async (StockAdjustmentRequest request, InventoryService inventory) =>
{
    var result = await inventory.AdjustStockAsync(
        request.ProductId,
        request.CountedStock,
        request.Reason);

    return result.Succeeded
        ? Results.Ok(new { result.Message })
        : Results.BadRequest(new { result.Message });
})
.WithName("AdjustStockToCount");

app.MapGet("/api/ai-usage", async (InventoryService inventory) =>
{
    var records = await inventory.GetAiTokenUsageRecordsAsync();
    return Results.Ok(records.Select(ToAiUsageResponse));
})
.WithName("GetAiUsage");

app.MapGet("/api/ai-usage/summary", async (DateTime? monthStart, InventoryService inventory) =>
{
    var summary = await inventory.GetAiUsageSummaryAsync(monthStart);
    return Results.Ok(summary);
})
.WithName("GetAiUsageSummary");

app.MapGet("/api/ai-budget", async (InventoryService inventory) =>
{
    var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var budgets = await inventory.GetAiMonthlyBudgetsAsync();
    var budget = budgets.FirstOrDefault(value => value.MonthStart == monthStart)
        ?? new inventory_admin.Models.AiMonthlyBudget
        {
            MonthStart = monthStart,
            BudgetUsd = 25m,
            WarningThresholdPercent = 80m,
            CriticalThresholdPercent = 100m
        };

    return Results.Ok(ToAiMonthlyBudgetResponse(budget));
})
.WithName("GetCurrentAiBudget");

app.MapPost("/api/ai-budget", async (AiBudgetSettingsRequest request, InventoryService inventory) =>
{
    var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var result = await inventory.SaveAiMonthlyBudgetAsync(new AiMonthlyBudgetRequest(
        monthStart,
        request.MonthlyBudgetUsd,
        request.WarningThresholdPercent,
        100m));

    return result.Succeeded
        ? Results.Ok(ToAiMonthlyBudgetResponse(result.Budget!))
        : Results.BadRequest(new { result.Message });
})
.WithName("SaveCurrentAiBudget");

app.MapGet("/api/ai-usage/monthly-budgets", async (InventoryService inventory) =>
{
    var budgets = await inventory.GetAiMonthlyBudgetsAsync();
    return Results.Ok(budgets.Select(ToAiMonthlyBudgetResponse));
})
.WithName("GetAiMonthlyBudgets");

app.MapPut("/api/ai-usage/monthly-budgets", async (AiMonthlyBudgetRequest request, InventoryService inventory) =>
{
    var result = await inventory.SaveAiMonthlyBudgetAsync(request);
    return result.Succeeded
        ? Results.Ok(ToAiMonthlyBudgetResponse(result.Budget!))
        : Results.BadRequest(new { result.Message });
})
.WithName("UpsertAiMonthlyBudget");

app.MapGet("/api/ai-model-prices", async (InventoryService inventory) =>
{
    var prices = await inventory.GetAiModelPricesAsync();
    return Results.Ok(prices.Select(ToAiModelPriceResponse));
})
.WithName("GetAiModelPrices");

app.MapPost("/api/ai-model-prices", async (AiModelPriceCreateRequest request, InventoryService inventory) =>
{
    var result = await inventory.SaveAiModelPriceAsync(request);
    return result.Succeeded
        ? Results.Ok(ToAiModelPriceResponse(result.Price!))
        : Results.BadRequest(new { result.Message });
})
.WithName("CreateAiModelPrice");

app.MapPost("/api/ai-usage", async (AiTokenUsageCreateRequest request, InventoryService inventory) =>
{
    var result = await inventory.SaveAiTokenUsageRecordAsync(request);
    return result.Succeeded
        ? Results.Ok(ToAiUsageResponse(result.Record!))
        : Results.BadRequest(new { result.Message });
})
.WithName("CreateAiUsage");

app.MapPost("/api/ai-usage/import", async (AiTokenUsageImportRequest request, InventoryService inventory) =>
{
    var result = await inventory.ImportAiTokenUsageAsync(request);
    return Results.Ok(result);
})
.WithName("ImportAiUsage");

app.MapPost("/api/ai-usage/import/preview", async (AiTokenUsageImportRequest request, InventoryService inventory) =>
{
    var result = await inventory.PreviewAiTokenUsageImportAsync(request);
    return Results.Ok(result);
})
.WithName("PreviewAiUsageImport");

app.Run();

static object ToProductResponse(inventory_admin.Models.Product product)
{
    return new
    {
        product.Id,
        product.Name,
        product.Sku,
        product.Description,
        product.CurrentStock,
        product.ReorderLevel,
        product.UnitPrice,
        Category = product.Category?.Name,
        Supplier = product.Supplier?.Name,
        IsLowStock = product.CurrentStock <= product.ReorderLevel
    };
}

static object ToMovementResponse(inventory_admin.Models.StockMovement movement)
{
    return new
    {
        movement.Id,
        Product = movement.Product?.Name,
        movement.MovementType,
        movement.Quantity,
        movement.Reason,
        movement.CreatedAt
    };
}

static object ToPurchaseOrderResponse(inventory_admin.Models.PurchaseOrder order)
{
    return new
    {
        order.Id,
        Status = order.Status.ToString(),
        order.CreatedAt,
        order.UpdatedAt,
        order.OrderedAt,
        order.ReceivedAt,
        order.CancelledAt,
        Supplier = order.Supplier is null
            ? null
            : new
            {
                order.Supplier.Id,
                order.Supplier.Name,
                order.Supplier.CountryCode
            },
        Lines = order.Lines
            .OrderBy(line => line.Product?.Name)
            .Select(line => new
            {
                line.Id,
                line.ProductId,
                Product = line.Product?.Name,
                line.Product?.Sku,
                line.OrderedQuantity,
                line.ReceivedQuantity,
                OpenQuantity = line.OrderedQuantity - line.ReceivedQuantity
            })
    };
}

static object ToAiUsageResponse(inventory_admin.Models.AiTokenUsageRecord record)
{
    return new
    {
        record.Id,
        record.AiModelPriceId,
        record.FeatureName,
        record.Provider,
        record.BillingProvider,
        record.UpstreamProvider,
        record.ModelName,
        record.OpenRouterModelSlug,
        record.RouteName,
        record.PromptTokens,
        record.CachedInputTokens,
        record.CompletionTokens,
        record.ReasoningTokens,
        record.ToolCallCount,
        record.SearchCallCount,
        record.TotalTokens,
        record.PromptCostPerMillionTokens,
        record.CachedInputCostPerMillionTokens,
        record.CompletionCostPerMillionTokens,
        record.SearchCostPerThousandCalls,
        record.EstimatedCostUsd,
        record.ActualCostUsd,
        record.EffectiveCostUsd,
        record.Notes,
        record.CreatedAt
    };
}

static object ToAiModelPriceResponse(inventory_admin.Models.AiModelPrice price)
{
    return new
    {
        price.Id,
        price.Provider,
        price.BillingProvider,
        price.UpstreamProvider,
        price.ModelName,
        price.OpenRouterModelSlug,
        price.RouteName,
        price.DisplayName,
        price.Currency,
        price.InputCostPerMillionTokens,
        price.CachedInputCostPerMillionTokens,
        price.OutputCostPerMillionTokens,
        price.SearchCostPerThousandCalls,
        price.EffectiveFrom,
        price.EffectiveTo,
        price.IsDefault,
        price.IsCustom,
        price.SourceUrl
    };
}

static object ToAiMonthlyBudgetResponse(inventory_admin.Models.AiMonthlyBudget budget)
{
    return new
    {
        budget.Id,
        budget.MonthStart,
        budget.BudgetUsd,
        budget.WarningThresholdPercent,
        budget.CriticalThresholdPercent,
        budget.CreatedAt,
        budget.UpdatedAt
    };
}
