using PosSystem.Domain.Common;
using PosSystem.Domain.Enums;

namespace PosSystem.Domain.Entities;

public class SaleReturn : BaseEntity
{
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public int ShiftId { get; set; }
    public CashierShift Shift { get; set; } = null!;
    public int ProcessedByUserId { get; set; }
    public User ProcessedByUser { get; set; } = null!;
    public DateTime RefundDate { get; set; } = DateTime.UtcNow;
    public PaymentMethod RefundMethod { get; set; }
    public string? Reason { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public List<SaleReturnItem> Items { get; set; } = new();
}
