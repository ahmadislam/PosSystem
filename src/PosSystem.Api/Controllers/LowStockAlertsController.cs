using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/operations/low-stock-alerts"), Authorize(Roles = "Admin,Manager")]
public sealed class LowStockAlertsController(IAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LowStockAlertDto>>> List(CancellationToken ct)
    {
        var alerts = await db.LowStockAlerts.IgnoreQueryFilters().AsNoTracking()
            .Include(alert => alert.Product)
            .OrderByDescending(alert => alert.DetectedAt)
            .Take(100)
            .Select(alert => new LowStockAlertDto(
                alert.Id, alert.Product.Sku, alert.Product.Name, alert.Product.StockQuantity,
                alert.Product.MinStockThreshold, alert.DetectedAt, alert.SentAt, alert.ResolvedAt,
                alert.LastAttemptAt, alert.DeliveryAttempts, alert.LastError))
            .ToListAsync(ct);
        return Ok(alerts);
    }
}
