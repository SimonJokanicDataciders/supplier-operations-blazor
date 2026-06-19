using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;

namespace inventory_admin.Services;

public partial class InventoryService
{
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
}
