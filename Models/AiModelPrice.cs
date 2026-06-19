using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class AiModelPrice
{
    public int Id { get; set; }

    [Required]
    [StringLength(80)]
    public string Provider { get; set; } = string.Empty;

    [StringLength(80)]
    public string? BillingProvider { get; set; }

    [StringLength(80)]
    public string? UpstreamProvider { get; set; }

    [Required]
    [StringLength(120)]
    public string ModelName { get; set; } = string.Empty;

    [StringLength(160)]
    public string? OpenRouterModelSlug { get; set; }

    [StringLength(120)]
    public string? RouteName { get; set; }

    [StringLength(3)]
    public string Currency { get; set; } = "USD";

    [Range(0, double.MaxValue)]
    public decimal InputCostPerMillionTokens { get; set; }

    [Range(0, double.MaxValue)]
    public decimal CachedInputCostPerMillionTokens { get; set; }

    [Range(0, double.MaxValue)]
    public decimal OutputCostPerMillionTokens { get; set; }

    [Range(0, double.MaxValue)]
    public decimal SearchCostPerThousandCalls { get; set; }

    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;

    public DateTime? EffectiveTo { get; set; }

    public bool IsDefault { get; set; }

    public bool IsCustom { get; set; }

    [StringLength(500)]
    public string? SourceUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<AiTokenUsageRecord> UsageRecords { get; set; } = [];

    public string DisplayName =>
        string.IsNullOrWhiteSpace(RouteName)
            ? $"{Provider} / {ModelName}"
            : $"{Provider} / {ModelName} / {RouteName}";
}
