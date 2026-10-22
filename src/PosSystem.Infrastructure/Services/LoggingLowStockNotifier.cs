using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosSystem.Application.Common;

namespace PosSystem.Infrastructure.Services;

/// <summary>Phase 1 placeholder: logs low stock. Phase 2 replaces this with a Hangfire job that emails the manager.</summary>
public class LoggingLowStockNotifier(IAppDbContext db, ILogger<LoggingLowStockNotifier> logger) : ILowStockNotifier
{
    public async Task CheckAsync(IEnumerable<int> productIds, CancellationToken ct = default)
    {
        var ids = productIds.ToList();
        var low = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.StockQuantity <= p.MinStockThreshold)
            .Select(p => new { p.Sku, p.StockQuantity, p.MinStockThreshold })
            .ToListAsync(ct);

        foreach (var p in low)
            logger.LogWarning("Low stock: {Sku} has {Qty} (threshold {Min})", p.Sku, p.StockQuantity, p.MinStockThreshold);
    }
}
