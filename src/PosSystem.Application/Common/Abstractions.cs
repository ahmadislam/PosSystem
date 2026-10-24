using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PosSystem.Domain.Entities;

namespace PosSystem.Application.Common;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Category> Categories { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Product> Products { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<CashierShift> CashierShifts { get; }
    DbSet<Sale> Sales { get; }
    DbSet<SaleItem> SaleItems { get; }
    DbSet<SaleReturn> SaleReturns { get; }
    DbSet<SaleReturnItem> SaleReturnItems { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Setting> Settings { get; }
    DbSet<LowStockAlert> LowStockAlerts { get; }
    DbSet<Customer> Customers { get; }
    DbSet<LoyaltyTransaction> LoyaltyTransactions { get; }
    DbSet<Coupon> Coupons { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<ProductImageContent> ProductImageContents { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ICurrentUser
{
    int? UserId { get; }
    string? Role { get; }
}

public interface ITokenService
{
    string CreateAccessToken(User user, out DateTime expiresAt);
    string GenerateRefreshToken();
    string HashToken(string rawToken);
}

/// <summary>Records or resolves low-stock alerts within the caller's business transaction.</summary>
public interface ILowStockNotifier
{
    Task CheckAsync(IEnumerable<int> productIds, CancellationToken ct = default);
}

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "PosSystem";
    public string Audience { get; set; } = "PosSystemClient";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public class DemoAccessSettings
{
    public bool Enabled { get; set; }
    public string Username { get; set; } = "portfolio-demo";
    public string Email { get; set; } = "portfolio-demo@example.invalid";
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
