using Hangfire;
using PosSystem.Application.Common;

namespace PosSystem.Infrastructure.Services;

public class HangfireLowStockNotifier(IBackgroundJobClient jobs) : ILowStockNotifier
{
    public Task CheckAsync(IEnumerable<int> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.CompletedTask;

        jobs.Enqueue<LowStockEmailJob>(job => job.SendAsync(ids, CancellationToken.None));
        return Task.CompletedTask;
    }
}
