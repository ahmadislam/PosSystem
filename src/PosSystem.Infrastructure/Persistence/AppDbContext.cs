using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PosSystem.Application.Common;
using PosSystem.Domain.Common;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;

namespace PosSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<LowStockAlert> LowStockAlerts => Set<LowStockAlert>();

    // Properties never written into the audit trail.
    private static readonly HashSet<string> AuditIgnored = ["PasswordHash", "RowVersion", "UpdatedAt", "CreatedAt"];

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        const string NotDeleted = "\"IsDeleted\" = false";

        b.HasSequence<int>("invoice_seq").StartsAt(1).IncrementsBy(1);

        b.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(50);
            e.Property(u => u.Email).HasMaxLength(200);
            e.HasIndex(u => u.Username).IsUnique().HasFilter(NotDeleted);
            e.HasIndex(u => u.Email).IsUnique().HasFilter(NotDeleted);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId);
        });

        b.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasIndex(c => c.Name).IsUnique().HasFilter(NotDeleted);
        });

        b.Entity<Supplier>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200);
            e.Property(s => s.ContactName).HasMaxLength(200);
            e.Property(s => s.Email).HasMaxLength(200);
            e.Property(s => s.Phone).HasMaxLength(50);
            e.Property(s => s.Address).HasMaxLength(500);
            e.Property(s => s.Notes).HasMaxLength(1000);
            e.HasIndex(s => s.Name).IsUnique().HasFilter(NotDeleted);
        });

        b.Entity<Product>(e =>
        {
            e.Property(p => p.Sku).HasMaxLength(50);
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.RowVersion).IsRowVersion();   // uint -> PostgreSQL xmin
            e.HasIndex(p => p.Sku).IsUnique().HasFilter(NotDeleted);
            e.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId);
            e.HasOne(p => p.DefaultSupplier).WithMany().HasForeignKey(p => p.DefaultSupplierId);
        });

        b.Entity<StockMovement>(e =>
        {
            e.HasIndex(m => new { m.ProductId, m.CreatedAt });
            e.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId);
            e.HasOne(m => m.CreatedByUser).WithMany().HasForeignKey(m => m.CreatedByUserId);
        });

        b.Entity<CashierShift>(e =>
        {
            // One active shift (Open=0, PendingClose=1) per cashier, enforced by the database.
            e.HasIndex(s => s.CashierId).IsUnique().HasFilter("\"Status\" IN (0, 1)");
            e.HasOne(s => s.Cashier).WithMany().HasForeignKey(s => s.CashierId);
        });

        b.Entity<Sale>(e =>
        {
            e.Property(s => s.InvoiceNumber).HasMaxLength(30);
            e.HasIndex(s => s.InvoiceNumber).IsUnique();
            e.HasIndex(s => s.SaleDate);
            e.HasIndex(s => new { s.CashierId, s.IdempotencyKey })
                .IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.Property(s => s.IdempotencyKey).HasMaxLength(32);
            e.Property(s => s.RequestHash).HasMaxLength(64);
            e.HasOne(s => s.Cashier).WithMany().HasForeignKey(s => s.CashierId);
            e.HasOne(s => s.Shift).WithMany().HasForeignKey(s => s.ShiftId);
            e.HasMany(s => s.Items).WithOne(i => i.Sale).HasForeignKey(i => i.SaleId);
        });

        b.Entity<SaleItem>(e => e.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId));

        b.Entity<PurchaseOrder>(e =>
        {
            e.Property(p => p.Version).IsConcurrencyToken();
            e.HasIndex(p => new { p.Status, p.CreatedAt });
            e.HasOne(p => p.Supplier).WithMany().HasForeignKey(p => p.SupplierId);
            e.HasOne(p => p.CreatedByUser).WithMany().HasForeignKey(p => p.CreatedByUserId);
            e.HasMany(p => p.Items).WithOne(i => i.PurchaseOrder).HasForeignKey(i => i.PurchaseOrderId);
        });

        b.Entity<PurchaseOrderItem>(e =>
        {
            e.HasIndex(i => i.PurchaseOrderId);
            e.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId);
        });

        b.Entity<SaleReturn>(e =>
        {
            e.HasIndex(r => new { r.SaleId, r.RefundDate });
            e.Property(r => r.Reason).HasMaxLength(500);
            e.HasOne(r => r.Sale).WithMany().HasForeignKey(r => r.SaleId);
            e.HasOne(r => r.Shift).WithMany().HasForeignKey(r => r.ShiftId);
            e.HasOne(r => r.ProcessedByUser).WithMany().HasForeignKey(r => r.ProcessedByUserId);
        });

        b.Entity<SaleReturnItem>(e =>
        {
            e.HasIndex(i => i.SaleItemId);
            e.HasOne(i => i.SaleReturn).WithMany(r => r.Items).HasForeignKey(i => i.SaleReturnId);
            e.HasOne(i => i.SaleItem).WithMany().HasForeignKey(i => i.SaleItemId);
            e.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => new { a.EntityName, a.EntityId });
            e.Property(a => a.OldValues).HasColumnType("jsonb");
            e.Property(a => a.NewValues).HasColumnType("jsonb");
        });

        b.Entity<Setting>(e => e.HasIndex(s => s.SettingKey).IsUnique());
        b.Entity<LowStockAlert>(e =>
        {
            e.Property(alert => alert.LastError).HasMaxLength(500);
            e.HasOne(alert => alert.Product).WithMany().HasForeignKey(alert => alert.ProductId);
            e.HasIndex(alert => alert.ProductId).IsUnique().HasFilter("\"ResolvedAt\" IS NULL");
            e.HasIndex(alert => new { alert.SentAt, alert.NextAttemptAt });
        });

        // Soft delete: hide IsDeleted rows from every query on entities implementing ISoftDelete.
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(ISoftDelete).IsAssignableFrom(t.ClrType)))
        {
            var param = Expression.Parameter(type.ClrType, "e");
            var isDeleted = Expression.Property(param, nameof(ISoftDelete.IsDeleted));
            type.SetQueryFilter(Expression.Lambda(Expression.Equal(isDeleted, Expression.Constant(false)), param));
        }

        // Nothing is physically deleted, so never cascade.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ChangeTracker.DetectChanges();
        ApplySoftDeleteAndTimestamps();
        var pending = CaptureAudit();
        if (pending.Count == 0)
            return await base.SaveChangesAsync(ct);

        var ownsTransaction = Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(ct) : null;
        try
        {
            var result = await base.SaveChangesAsync(ct);
            foreach (var p in pending)
            {
                AuditLogs.Add(new AuditLog
                {
                    UserId = currentUser.UserId,
                    Timestamp = DateTime.UtcNow,
                    Action = p.Action,
                    EntityName = p.Entry.Metadata.ClrType.Name,
                    EntityId = ((BaseEntity)p.Entry.Entity).Id.ToString(),
                    OldValues = p.Old,
                    NewValues = p.New
                });
            }
            await base.SaveChangesAsync(ct);
            if (ownsTransaction)
                await transaction!.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (ownsTransaction)
                await transaction!.RollbackAsync(ct);
            throw;
        }
    }

    private void ApplySoftDeleteAndTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>().ToList())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDelete)
            {
                entry.State = EntityState.Unchanged;
                entry.Property(nameof(ISoftDelete.IsDeleted)).CurrentValue = true;   // marks only this column modified
            }
            if (entry.State == EntityState.Modified)
                entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = now;
        }
    }

    private sealed record PendingAudit(EntityEntry Entry, AuditAction Action, string? Old, string? New);

    private List<PendingAudit> CaptureAudit()
    {
        var list = new List<PendingAudit>();
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.Entity is AuditLog or RefreshToken) continue;

            if (entry.State == EntityState.Added)
            {
                var values = entry.Properties
                    .Where(p => !AuditIgnored.Contains(p.Metadata.Name) && !p.Metadata.IsPrimaryKey())
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                list.Add(new PendingAudit(entry, AuditAction.Create, null, JsonSerializer.Serialize(values)));
            }
            else if (entry.State == EntityState.Modified)
            {
                var changed = entry.Properties
                    .Where(p => p.IsModified && !AuditIgnored.Contains(p.Metadata.Name)).ToList();
                if (changed.Count == 0) continue;

                var softDeleted = entry.Entity is ISoftDelete sd && sd.IsDeleted
                    && changed.Any(p => p.Metadata.Name == nameof(ISoftDelete.IsDeleted));
                list.Add(new PendingAudit(entry,
                    softDeleted ? AuditAction.Delete : AuditAction.Update,
                    JsonSerializer.Serialize(changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue)),
                    JsonSerializer.Serialize(changed.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue))));
            }
        }
        return list;
    }
}
