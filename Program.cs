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
        summary.InventoryValue,
        summary.SupplierOverview,
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
