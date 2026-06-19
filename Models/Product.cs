using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class Product
{
    public int Id { get; set; }

    [Required]
    [StringLength(180)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string Sku { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; set; }

    [Range(0, int.MaxValue)]
    public int CurrentStock { get; set; }

    [Range(0, int.MaxValue)]
    public int ReorderLevel { get; set; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Category? Category { get; set; }

    public Supplier? Supplier { get; set; }

    public List<StockMovement> StockMovements { get; set; } = [];
}
