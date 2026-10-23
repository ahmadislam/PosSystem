using System.ComponentModel.DataAnnotations;
using PosSystem.Domain.Enums;

namespace PosSystem.Application.Dtos;

// ---- Auth ----
public class LoginRequest
{
    [Required] public string Username { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}
public record AuthResponse(string AccessToken, DateTime ExpiresAt, string Username, string Role);

// ---- Staff ----
public class CreateStaffRequest
{
    [Required, StringLength(50, MinimumLength = 3)] public string Username { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = string.Empty;
    [Required, MinLength(12), StringLength(128)] public string Password { get; set; } = string.Empty;
    [EnumDataType(typeof(UserRole))] public UserRole Role { get; set; } = UserRole.Cashier;
}
public class UpdateStaffRequest
{
    [EnumDataType(typeof(UserRole))] public UserRole Role { get; set; }
    public bool IsActive { get; set; }
}
public class ResetStaffPasswordRequest
{
    [Required, MinLength(12), StringLength(128)] public string Password { get; set; } = string.Empty;
}
public record StaffDto(int Id, string Username, string Email, UserRole Role, bool IsActive, DateTime CreatedAt);

public class UpdateEmailSettingsRequest
{
    [Required, EmailAddress, StringLength(200)] public string ManagerEmail { get; set; } = string.Empty;
    public bool AlertsEnabled { get; set; }
}
public record EmailSettingsDto(bool SmtpEnabled, string? ManagerEmail, bool AlertsEnabled);

// ---- Categories ----
public class CategoryRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
}
public record CategoryDto(int Id, string Name, string? Description);

// ---- Products ----
public class ProductRequest
{
    [Required, StringLength(50)] public string Sku { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(1000)] public string? Description { get; set; }
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    [Range(1, int.MaxValue)] public int? DefaultSupplierId { get; set; }
    [Range(0, 1000000000)] public decimal CostPrice { get; set; }
    [Range(0, 1000000000)] public decimal SalePrice { get; set; }
    [Range(0, 100)] public decimal TaxRate { get; set; }
    /// <summary>Initial stock on create. Ignored on update: use the adjust-stock endpoint.</summary>
    [Range(0, int.MaxValue)] public int StockQuantity { get; set; }
    [Range(0, int.MaxValue)] public int MinStockThreshold { get; set; }
    public bool IsActive { get; set; } = true;
}
public class StockAdjustRequest
{
    [Range(-1000000, 1000000)] public int QuantityChange { get; set; }
    [Required, StringLength(300)] public string Note { get; set; } = string.Empty;
}
public record ProductDto(int Id, string Sku, string Name, string? Description, int CategoryId, string CategoryName,
    int? DefaultSupplierId, decimal CostPrice, decimal SalePrice, decimal TaxRate, int StockQuantity, int MinStockThreshold,
    bool IsActive, bool IsLowStock, string? PrimaryImageUrl = null);

public class CustomerRequest
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = string.Empty;
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}
public record CustomerDto(int Id, string Name, string? Email, string? Phone, string? Address,
    int LoyaltyPoints, bool IsActive);
public record LoyaltyTransactionDto(int Id, DateTime CreatedAt, int PointsChange, int BalanceAfter,
    string Type, string? Note);
public record ProductImageDto(int Id, string ContentType, bool IsPrimary, string ImageUrl);
public class CouponRequest
{
    [Required, StringLength(40, MinimumLength = 2)] public string Code { get; set; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = string.Empty;
    public bool IsPercentage { get; set; }
    [Range(0.01, 1000000000)] public decimal Value { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(1, 100000000)] public int? MaxRedemptions { get; set; }
}
public record CouponDto(int Id, string Code, string Name, bool IsPercentage, decimal Value,
    DateTime StartsAt, DateTime EndsAt, bool IsActive, int? MaxRedemptions, int RedemptionCount);
public class LoyaltyRulesRequest
{
    [Range(1, 1000)] public int EarnPointsPerCurrencyUnit { get; set; } = 1;
    [Range(1, 10000)] public int RedemptionPointsPerCurrencyUnit { get; set; } = 100;
}
public record LoyaltyRulesDto(int EarnPointsPerCurrencyUnit, int RedemptionPointsPerCurrencyUnit);

// ---- Suppliers ----
public class SupplierRequest
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = string.Empty;
    [StringLength(200)] public string? ContactName { get; set; }
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
public record SupplierDto(int Id, string Name, string? ContactName, string? Email, string? Phone,
    string? Address, string? Notes, bool IsActive);

// ---- Purchase orders ----
public class PurchaseOrderLineRequest
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 1000000)] public int Quantity { get; set; }
    [Range(0, 1000000000)] public decimal UnitCost { get; set; }
}
public class PurchaseOrderRequest
{
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [Required, MinLength(1), MaxLength(200)] public List<PurchaseOrderLineRequest> Items { get; set; } = [];
}
public record PurchaseOrderItemDto(int Id, int ProductId, string Sku, string ProductName,
    int Quantity, decimal UnitCost, decimal LineTotal);
public record PurchaseOrderDto(int Id, int SupplierId, string SupplierName, int CreatedByUserId,
    PurchaseOrderStatus Status, DateTime CreatedAt, DateTime? OrderedAt, DateTime? ReceivedAt,
    decimal TotalCost, IReadOnlyList<PurchaseOrderItemDto> Items);

// ---- Shifts ----
public class OpenShiftRequest { [Range(0, 1000000000)] public decimal OpeningCash { get; set; } }
public class CloseShiftRequest { [Range(0, 1000000000)] public decimal ClosingCash { get; set; } }
public record ShiftDto(int Id, int CashierId, DateTime StartTime, DateTime? EndTime, decimal OpeningCash,
    decimal ExpectedCash, decimal? ClosingCash, decimal? CashVariance, ShiftStatus Status);

// ---- Sales ----
public class SaleLineRequest
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 100000)] public int Quantity { get; set; }
    [Range(0, 1000000000)] public decimal DiscountAmount { get; set; }
}
public class CreateSaleRequest
{
    public PaymentMethod PaymentMethod { get; set; }
    [Range(0, 1000000000)] public decimal? CashTendered { get; set; }
    [Range(1, int.MaxValue)] public int? CustomerId { get; set; }
    [StringLength(40)] public string? CouponCode { get; set; }
    [Range(0, 100000000)] public int LoyaltyPointsToRedeem { get; set; }
    [Required, MinLength(1)] public List<SaleLineRequest> Items { get; set; } = new();
}
public record SaleItemDto(int SaleItemId, int ProductId, string Sku, string ProductName, int Quantity, decimal UnitPrice,
    decimal DiscountAmount, decimal TaxAmount, decimal LineTotal);
public record SaleDto(int Id, string InvoiceNumber, DateTime SaleDate, decimal Subtotal, decimal DiscountAmount,
    decimal TaxAmount, decimal TotalAmount, PaymentMethod PaymentMethod, SaleStatus Status,
    IReadOnlyList<SaleItemDto> Items, int? CustomerId = null, string? CustomerName = null, string? CouponCode = null,
    int LoyaltyPointsEarned = 0, int LoyaltyPointsRedeemed = 0,
    decimal? CashTendered = null, decimal? ChangeDue = null);

public class SaleReturnLineRequest
{
    [Range(1, int.MaxValue)] public int SaleItemId { get; set; }
    [Range(1, 100000)] public int Quantity { get; set; }
}
public class CreateSaleReturnRequest
{
    public PaymentMethod RefundMethod { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    [Required, MinLength(1)] public List<SaleReturnLineRequest> Items { get; set; } = new();
}
public record SaleReturnItemDto(int SaleItemId, int ProductId, string Sku, string ProductName, int Quantity,
    decimal UnitPrice, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal);
public record SaleReturnDto(int Id, int SaleId, int ShiftId, int ProcessedByUserId, DateTime RefundDate,
    PaymentMethod RefundMethod, string? Reason, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal TotalAmount,
    IReadOnlyList<SaleReturnItemDto> Items);

// ---- Dashboard ----
public record DailySalesDto(DateTime Date, decimal Revenue);
public record MonthlyRevenueDto(DateTime Month, decimal Revenue);
public record TopSellingProductDto(string Sku, string ProductName, int QuantitySold, decimal Revenue);
public record CategorySalesDto(int CategoryId, string CategoryName, int QuantitySold, decimal Revenue);
public record DashboardSummaryDto(
    int TotalSales,
    decimal Revenue,
    decimal GrossSales,
    decimal Refunds,
    decimal NetRevenue,
    int LowStockCount,
    IReadOnlyList<DailySalesDto> DailySales,
    IReadOnlyList<MonthlyRevenueDto> MonthlyRevenue,
    IReadOnlyList<TopSellingProductDto> TopSellingProducts,
    IReadOnlyList<CategorySalesDto> SalesByCategory,
    IReadOnlyList<SaleDto> RecentSales,
    IReadOnlyList<ProductDto> LowStockProducts);

// ---- History ----
public record AuditLogDto(int Id, DateTime Timestamp, int? UserId, string? Username, AuditAction Action,
    string EntityName, string EntityId, string? OldValues, string? NewValues);
public record StockMovementDto(int Id, DateTime CreatedAt, int ProductId, string Sku, string ProductName,
    int CreatedByUserId, string? Username, StockMovementType Type, int QuantityChange,
    string? ReferenceType, int? ReferenceId, string? Note);
public record LowStockAlertDto(int Id, string Sku, string ProductName, int StockQuantity,
    int MinStockThreshold, DateTime DetectedAt, DateTime? SentAt, DateTime? ResolvedAt,
    DateTime? LastAttemptAt, int DeliveryAttempts, string? LastError);
