namespace inventory_admin.Dtos;

public sealed record AiTokenUsageCreateRequest(
    string FeatureName,
    string Provider,
    string BillingProvider,
    string? UpstreamProvider,
    string ModelName,
    string? OpenRouterModelSlug,
    string? RouteName,
    int? AiModelPriceId,
    int PromptTokens,
    int CachedInputTokens,
    int CompletionTokens,
    int ReasoningTokens,
    int ToolCallCount,
    int SearchCallCount,
    decimal PromptCostPerMillionTokens,
    decimal CachedInputCostPerMillionTokens,
    decimal CompletionCostPerMillionTokens,
    decimal SearchCostPerThousandCalls,
    decimal? ActualCostUsd,
    DateTime? CreatedAt,
    string? Notes);

public sealed record AiTokenUsageImportRequest(
    string Content,
    string? Format);

public sealed record AiTokenUsageImportResult(
    int ImportedCount,
    IReadOnlyList<AiTokenUsageImportError> Errors);

public sealed record AiTokenUsageImportError(
    int RowNumber,
    string Message);

public sealed record AiBudgetSettingsRequest(
    decimal MonthlyBudgetUsd,
    decimal WarningThresholdPercent);

public sealed record AiModelPriceCreateRequest(
    string Provider,
    string? BillingProvider,
    string? UpstreamProvider,
    string ModelName,
    string? OpenRouterModelSlug,
    string? RouteName,
    decimal InputCostPerMillionTokens,
    decimal CachedInputCostPerMillionTokens,
    decimal OutputCostPerMillionTokens,
    decimal SearchCostPerThousandCalls,
    DateTime? EffectiveFrom,
    string? SourceUrl);

public sealed record AiMonthlyBudgetRequest(
    DateTime MonthStart,
    decimal BudgetUsd,
    decimal WarningThresholdPercent,
    decimal CriticalThresholdPercent);

public sealed record AiUsageSummaryRequest(DateTime? MonthStart);

public sealed record AiUsageSummaryDto(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int RequestCount,
    int PromptTokens,
    int CachedInputTokens,
    int CompletionTokens,
    int ReasoningTokens,
    int TotalTokens,
    int ToolCallCount,
    int SearchCallCount,
    decimal EstimatedCostUsd,
    decimal ActualCostUsd,
    decimal EffectiveCostUsd,
    decimal EstimatedVsActualDeltaUsd,
    AiBudgetStatusDto? Budget,
    IReadOnlyList<AiUsageGroupDto> ByFeature,
    IReadOnlyList<AiUsageGroupDto> ByProvider,
    IReadOnlyList<AiUsageGroupDto> ByModel,
    IReadOnlyList<AiUsageGroupDto> ByRoute);

public sealed record AiUsageGroupDto(
    string Key,
    string? Provider,
    string? BillingProvider,
    string? UpstreamProvider,
    string? ModelName,
    string? OpenRouterModelSlug,
    string? RouteName,
    int RequestCount,
    int TotalTokens,
    int SearchCallCount,
    decimal EstimatedCostUsd,
    decimal ActualCostUsd,
    decimal EffectiveCostUsd);

public sealed record AiBudgetStatusDto(
    int Id,
    DateTime MonthStart,
    decimal BudgetUsd,
    decimal WarningThresholdPercent,
    decimal CriticalThresholdPercent,
    decimal EffectiveSpendUsd,
    decimal RemainingUsd,
    decimal PercentUsed,
    bool IsWarning,
    bool IsCritical,
    string Status);
