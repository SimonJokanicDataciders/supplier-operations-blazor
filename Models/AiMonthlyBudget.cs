using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class AiMonthlyBudget
{
    public int Id { get; set; }

    public DateTime MonthStart { get; set; }

    [Range(0, double.MaxValue)]
    public decimal BudgetUsd { get; set; }

    [Range(0, 100)]
    public decimal WarningThresholdPercent { get; set; } = 80m;

    [Range(0, 100)]
    public decimal CriticalThresholdPercent { get; set; } = 100m;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
