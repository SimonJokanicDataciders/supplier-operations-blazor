using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace inventory_admin.Models;

public class AiTokenUsageRecord
{
    public int Id { get; set; }

    public int? AiModelPriceId { get; set; }

    [Required]
    [StringLength(120)]
    public string FeatureName { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string Provider { get; set; } = "OpenAI";

    [Required]
    [StringLength(80)]
    public string BillingProvider { get; set; } = "OpenAI";

    [StringLength(80)]
    public string? UpstreamProvider { get; set; }

    [Required]
    [StringLength(120)]
    public string ModelName { get; set; } = string.Empty;

    [StringLength(160)]
    public string? OpenRouterModelSlug { get; set; }

    [StringLength(120)]
    public string? RouteName { get; set; }

    [Range(0, int.MaxValue)]
    public int PromptTokens { get; set; }

    [Range(0, int.MaxValue)]
    public int CachedInputTokens { get; set; }

    [Range(0, int.MaxValue)]
    public int CompletionTokens { get; set; }

    [Range(0, int.MaxValue)]
    public int ReasoningTokens { get; set; }

    [Range(0, int.MaxValue)]
    public int ToolCallCount { get; set; }

    [Range(0, int.MaxValue)]
    public int SearchCallCount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal PromptCostPerMillionTokens { get; set; }

    [Range(0, double.MaxValue)]
    public decimal CachedInputCostPerMillionTokens { get; set; }

    [Range(0, double.MaxValue)]
    public decimal CompletionCostPerMillionTokens { get; set; }

    [Range(0, double.MaxValue)]
    public decimal EstimatedCostUsd { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? ActualCostUsd { get; set; }

    [Range(0, double.MaxValue)]
    public decimal SearchCostPerThousandCalls { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AiModelPrice? AiModelPrice { get; set; }

    [NotMapped]
    public int TotalTokens => PromptTokens + CachedInputTokens + CompletionTokens;

    [NotMapped]
    public decimal EffectiveCostUsd => ActualCostUsd ?? EstimatedCostUsd;
}
