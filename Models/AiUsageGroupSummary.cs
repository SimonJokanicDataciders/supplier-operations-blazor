namespace inventory_admin.Models;

public sealed record AiUsageGroupSummary(
    string Name,
    int RequestCount,
    int TotalTokens,
    decimal EstimatedCostUsd,
    decimal ActualCostUsd);
