using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/reports"), Authorize(Roles = "Admin,Manager")]
public class ReportsController(IExcelReportService reports) : ControllerBase
{
    [HttpGet("inventory.xlsx")]
    public async Task<IActionResult> Inventory(CancellationToken ct)
    {
        var bytes = await reports.CreateInventoryReportAsync(ct);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"inventory-report-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("sales.xlsx")]
    public async Task<IActionResult> Sales(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var end = to?.Date ?? DateTime.UtcNow.Date;
        var start = from?.Date ?? end.AddDays(-29);
        if (start > end) return BadRequest("'from' must be on or before 'to'.");

        var bytes = await reports.CreateSalesReportAsync(start, end, ct);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"sales-report-{start:yyyyMMdd}-{end:yyyyMMdd}.xlsx");
    }
}
