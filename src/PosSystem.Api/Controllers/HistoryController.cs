using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Enums;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/history"), Authorize(Roles = "Admin,Manager")]
public class HistoryController(IHistoryService history, DemoAccessSettings demoAccess) : ControllerBase
{
    [HttpGet("audit")]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> Audit(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? entityName,
        [FromQuery] AuditAction? action,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await history.ListAuditAsync(from, to, entityName, action, page, pageSize, ct);
        if (!IsDemo()) return Ok(result);

        var sanitized = result.Items.Select(entry =>
            entry.EntityName is "User" or "Setting"
                ? entry with { OldValues = null, NewValues = null }
                : entry).ToArray();
        return Ok(result with { Items = sanitized });
    }

    [HttpGet("stock-movements")]
    public async Task<ActionResult<PagedResult<StockMovementDto>>> StockMovements(
        [FromQuery] int? productId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await history.ListStockMovementsAsync(productId, from, to, page, pageSize, ct));

    private bool IsDemo() => demoAccess.Enabled && string.Equals(
        User.FindFirst("name")?.Value,
        demoAccess.Username,
        StringComparison.OrdinalIgnoreCase);
}
