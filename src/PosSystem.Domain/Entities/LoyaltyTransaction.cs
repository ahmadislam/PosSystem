using PosSystem.Domain.Common;

namespace PosSystem.Domain.Entities;

public class LoyaltyTransaction : BaseEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }
    public int? SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }
    public int PointsChange { get; set; }
    public int BalanceAfter { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Note { get; set; }
}
