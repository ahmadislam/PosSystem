using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PosSystem.Application.Common;

namespace PosSystem.Infrastructure.Services;

public class HangfireLowStockNotifier(
    IAppDbContext db,
    IBackgroundJobClient jobs,
    IOptions<EmailOptions> options) : ILowStockNotifier
{
    public async Task CheckAsync(IEnumerable<int> productIds, CancellationToken ct = default)
    {
        if (!options.Value.Enabled) return;

        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return;

        var lowStockIds = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.StockQuantity <= p.MinStockThreshold)
            .Select(p => p.Id)
            .ToArrayAsync(ct);
        if (lowStockIds.Length == 0) return;

        jobs.Enqueue<LowStockEmailJob>(job => job.SendAsync(lowStockIds, CancellationToken.None));
    }
}
