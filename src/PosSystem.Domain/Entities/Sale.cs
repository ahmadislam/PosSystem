using PosSystem.Domain.Common;
using PosSystem.Domain.Enums;

namespace PosSystem.Domain.Entities;

public class Sale : BaseEntity, ISoftDelete
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public int CashierId { get; set; }
    public User Cashier { get; set; } = null!;
    public int ShiftId { get; set; }
    public CashierShift Shift { get; set; } = null!;
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }
    public int LoyaltyPointsEarned { get; set; }
    public int LoyaltyPointsRedeemed { get; set; }
    public DateTime SaleDate { get; set; } = DateTime.UtcNow;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    public bool IsDeleted { get; set; }
    public List<SaleItem> Items { get; set; } = new();
}
