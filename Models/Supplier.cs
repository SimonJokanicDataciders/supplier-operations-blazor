using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class Supplier
{
    public int Id { get; set; }

    [Required]
    [StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string CountryCode { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(254)]
    public string? ContactEmail { get; set; }

    [Url]
    [StringLength(500)]
    public string? WebsiteUrl { get; set; }

    [StringLength(120)]
    public string? Industry { get; set; }

    [StringLength(2000)]
    public string? OperationalNotes { get; set; }

    [StringLength(80)]
    public string? SourceSystem { get; set; }

    [StringLength(120)]
    public string? ExternalSupplierKey { get; set; }

    [Url]
    [StringLength(500)]
    public string? ResearchUrl { get; set; }

    public DateTime? ImportedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Product> Products { get; set; } = [];

    public List<PurchaseOrder> PurchaseOrders { get; set; } = [];
}
