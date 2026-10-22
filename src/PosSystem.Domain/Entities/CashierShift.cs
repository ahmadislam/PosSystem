using PosSystem.Domain.Common;
using PosSystem.Domain.Enums;

namespace PosSystem.Domain.Entities;

public class CashierShift : BaseEntity
{
    public int CashierId { get; set; }
    public User Cashier { get; set; } = null!;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public decimal OpeningCash { get; set; }
    /// <summary>Opening cash + cash sales - cash refunds.</summary>
    public decimal ExpectedCash { get; set; }
    public decimal? ClosingCash { get; set; }
    public decimal? CashVariance { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;
}
