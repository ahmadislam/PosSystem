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
    decimal CostPrice, decimal SalePrice, decimal TaxRate, int StockQuantity, int MinStockThreshold,
    bool IsActive, bool IsLowStock);

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
    [Required, MinLength(1)] public List<SaleLineRequest> Items { get; set; } = new();
}
public record SaleItemDto(int ProductId, string Sku, string ProductName, int Quantity, decimal UnitPrice,
    decimal DiscountAmount, decimal TaxAmount, decimal LineTotal);
public record SaleDto(int Id, string InvoiceNumber, DateTime SaleDate, decimal Subtotal, decimal DiscountAmount,
    decimal TaxAmount, decimal TotalAmount, PaymentMethod PaymentMethod, SaleStatus Status,
    IReadOnlyList<SaleItemDto> Items);
