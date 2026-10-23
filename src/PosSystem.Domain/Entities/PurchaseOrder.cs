using PosSystem.Domain.Common;
using PosSystem.Domain.Enums;

namespace PosSystem.Domain.Entities;

public class PurchaseOrder : BaseEntity
{
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateTime? OrderedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int Version { get; set; }
    public List<PurchaseOrderItem> Items { get; set; } = [];
}
