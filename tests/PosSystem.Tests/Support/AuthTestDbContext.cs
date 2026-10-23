using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Domain.Entities;

namespace PosSystem.Tests.Support;

internal sealed class AuthTestDbContext(DbContextOptions<AuthTestDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Category> Categories => throw new NotSupportedException();
    public DbSet<Supplier> Suppliers => throw new NotSupportedException();
    public DbSet<Product> Products => throw new NotSupportedException();
    public DbSet<PurchaseOrder> PurchaseOrders => throw new NotSupportedException();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => throw new NotSupportedException();
    public DbSet<StockMovement> StockMovements => throw new NotSupportedException();
    public DbSet<CashierShift> CashierShifts => throw new NotSupportedException();
    public DbSet<Sale> Sales => throw new NotSupportedException();
    public DbSet<SaleItem> SaleItems => throw new NotSupportedException();
    public DbSet<SaleReturn> SaleReturns => throw new NotSupportedException();
    public DbSet<SaleReturnItem> SaleReturnItems => throw new NotSupportedException();
    public DbSet<AuditLog> AuditLogs => throw new NotSupportedException();
    public DbSet<Setting> Settings => throw new NotSupportedException();
    public DbSet<LowStockAlert> LowStockAlerts => throw new NotSupportedException();
    public DbSet<Customer> Customers => throw new NotSupportedException();
    public DbSet<LoyaltyTransaction> LoyaltyTransactions => throw new NotSupportedException();
    public DbSet<Coupon> Coupons => throw new NotSupportedException();
    public DbSet<ProductImage> ProductImages => throw new NotSupportedException();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasKey(user => user.Id);
        modelBuilder.Entity<RefreshToken>().HasKey(token => token.Id);
        modelBuilder.Entity<RefreshToken>().HasIndex(token => token.TokenHash).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasOne(token => token.User).WithMany().HasForeignKey(token => token.UserId);
    }
}
