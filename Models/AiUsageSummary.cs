namespace inventory_admin.Models;

public sealed record AiUsageSummary(
    int RequestCount,
    int CurrentMonthRequestCount,
    int CurrentMonthTokens,
    decimal CurrentMonthEstimatedCostUsd,
    decimal CurrentMonthActualCostUsd,
    decimal MonthlyBudgetUsd,
    decimal WarningThresholdPercent,
    decimal BudgetUsedPercent,
    bool IsNearBudget,
    bool IsOverBudget,
    string? TopModel,
    string? TopFeature,
    DateTime? LastRunAt,
    IReadOnlyList<AiUsageGroupSummary> FeatureRows,
    IReadOnlyList<AiUsageGroupSummary> ProviderRows,
    IReadOnlyList<AiUsageGroupSummary> ModelRows);
