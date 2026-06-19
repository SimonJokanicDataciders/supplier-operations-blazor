using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class PurchaseOrderLine
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int PurchaseOrderId { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int OrderedQuantity { get; set; }

    [Range(0, int.MaxValue)]
    public int ReceivedQuantity { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }

    public Product? Product { get; set; }
}
