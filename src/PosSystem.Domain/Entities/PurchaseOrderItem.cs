using PosSystem.Domain.Common;

namespace PosSystem.Domain.Entities;

public class PurchaseOrderItem : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
}
