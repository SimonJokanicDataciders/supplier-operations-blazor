using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class AiBudgetSettings
{
    public int Id { get; set; } = 1;

    [Range(0, double.MaxValue)]
    public decimal MonthlyBudgetUsd { get; set; } = 25m;

    [Range(0, 100)]
    public decimal WarningThresholdPercent { get; set; } = 80m;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
