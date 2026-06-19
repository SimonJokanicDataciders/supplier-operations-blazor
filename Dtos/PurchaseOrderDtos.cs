namespace inventory_admin.Dtos;

public sealed record PurchaseOrderLineInput(int ProductId, int OrderedQuantity);

public sealed record PurchaseOrderCreateRequest(int SupplierId, IReadOnlyList<PurchaseOrderLineInput>? Lines);

public sealed record PurchaseOrderReceiveLineInput(int LineId, int Quantity);

public sealed record PurchaseOrderReceiveRequest(IReadOnlyList<PurchaseOrderReceiveLineInput>? Lines);
