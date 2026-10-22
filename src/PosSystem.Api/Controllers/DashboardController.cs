using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/dashboard"), Authorize(Roles = "Admin,Manager")]
public class DashboardController(IDashboardService dashboard) : ControllerBase
{
    /// <param name="from">Start date (yyyy-MM-dd), inclusive.</param>
    /// <param name="to">End date (yyyy-MM-dd), inclusive.</param>
    [HttpGet]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct) =>
        Ok(await dashboard.GetSummaryAsync(from, to, ct));
}
