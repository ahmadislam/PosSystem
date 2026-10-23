using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PosSystem.Domain.Entities;

namespace PosSystem.Application.Common;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<CashierShift> CashierShifts { get; }
    DbSet<Sale> Sales { get; }
    DbSet<SaleItem> SaleItems { get; }
    DbSet<SaleReturn> SaleReturns { get; }
    DbSet<SaleReturnItem> SaleReturnItems { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Setting> Settings { get; }
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

/// <summary>Called after a sale commits to enqueue alerts for products that are now below threshold.</summary>
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

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
