using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class PurchaseOrder
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? OrderedAt { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public Supplier? Supplier { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } = [];
}
