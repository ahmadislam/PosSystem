using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Domain.Entities;

namespace PosSystem.Infrastructure.Services;

public class HangfireLowStockNotifier(IAppDbContext db) : ILowStockNotifier
{
    public async Task CheckAsync(IEnumerable<int> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return;

        var products = await db.Products.IgnoreQueryFilters()
            .Where(p => ids.Contains(p.Id))
            .ToArrayAsync(ct);
        var alerts = await db.LowStockAlerts
            .Where(alert => ids.Contains(alert.ProductId) && alert.ResolvedAt == null)
            .ToDictionaryAsync(alert => alert.ProductId, ct);
        var now = DateTime.UtcNow;
        foreach (var product in products)
        {
            if (product.StockQuantity <= product.MinStockThreshold)
            {
                if (!alerts.ContainsKey(product.Id))
                    db.LowStockAlerts.Add(new LowStockAlert
                    {
                        ProductId = product.Id,
                        DetectedAt = now,
                        NextAttemptAt = now
                    });
            }
            else if (alerts.TryGetValue(product.Id, out var active))
            {
                active.ResolvedAt = now;
                active.LastError = null;
            }
        }
    }
}
