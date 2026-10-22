using PosSystem.Domain.Common;
using PosSystem.Domain.Enums;

namespace PosSystem.Domain.Entities;

public class StockMovement : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public StockMovementType Type { get; set; }
    /// <summary>Negative for stock going out.</summary>
    public int QuantityChange { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? Note { get; set; }
}
