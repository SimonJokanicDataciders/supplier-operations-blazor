using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Tests;

public sealed class AiUsageTests
{
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

    [Fact]
    public async Task PreviewAiTokenUsageImportAsync_MarksLikelyDuplicatesAsUnselected()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        await fixture.Service.SaveAiTokenUsageRecordAsync(new AiTokenUsageRecord
        {
            FeatureName = "Existing run",
            Provider = "OpenAI",
            BillingProvider = "OpenAI",
            ModelName = "gpt-test",
            PromptTokens = 100,
            CompletionTokens = 50,
            ActualCostUsd = 0.01m,
            CreatedAt = new DateTime(2026, 6, 4, 10, 0, 0, DateTimeKind.Utc)
        });

        const string csv = """
            feature,provider,model,input_tokens,output_tokens,actual_cost_usd,created_at
            Imported run,OpenAI,gpt-test,100,50,0.01,2026-06-04T10:00:00Z
            """;

        var preview = await fixture.Service.PreviewAiTokenUsageImportAsync(new AiTokenUsageImportRequest(csv, "csv"));
        var row = Assert.Single(preview.Rows);

        Assert.True(row.IsValid);
        Assert.True(row.IsDuplicate);
        Assert.False(row.IsSelected);
    }

    [Fact]
    public async Task ImportAiTokenUsageAsync_ImportsOnlySelectedPreviewRows()
    {
        await using var fixture = await InventoryServiceFixture.CreateAsync();
        const string csv = """
            feature,provider,model,input_tokens,output_tokens,actual_cost_usd,created_at
            First run,OpenAI,gpt-test,100,50,0.01,2026-06-05T10:00:00Z
            Second run,OpenRouter,anthropic/claude-test,200,75,0.03,2026-06-05T11:00:00Z
            """;

        var result = await fixture.Service.ImportAiTokenUsageAsync(new AiTokenUsageImportRequest(csv, "csv", [3]));
        var record = await fixture.Db.AiTokenUsageRecords.SingleAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Empty(result.Errors);
        Assert.Equal("Second run", record.FeatureName);
        Assert.Equal("OpenRouter", record.Provider);
        Assert.Equal("anthropic/claude-test", record.ModelName);
    }
}
