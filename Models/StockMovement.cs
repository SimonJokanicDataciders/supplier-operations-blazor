using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class StockMovement
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    public StockMovementType MovementType { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Product? Product { get; set; }
}
