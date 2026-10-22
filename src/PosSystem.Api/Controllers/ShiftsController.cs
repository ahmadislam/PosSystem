using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/shifts"), Authorize]
public class ShiftsController(IShiftService shifts) : ControllerBase
{
    [HttpPost("open")]
    public async Task<ActionResult<ShiftDto>> Open(OpenShiftRequest request, CancellationToken ct) =>
        Ok(await shifts.OpenAsync(request.OpeningCash, ct));

    [HttpGet("current")]
    public async Task<ActionResult<ShiftDto>> Current(CancellationToken ct)
    {
        var shift = await shifts.GetCurrentAsync(ct);
        return shift is null ? NoContent() : Ok(shift);
    }

    /// <summary>Step 1 of closing: Open -> PendingClose (calculates expected cash).</summary>
    [HttpPost("end")]
    public async Task<ActionResult<ShiftDto>> End(CancellationToken ct) => Ok(await shifts.BeginCloseAsync(ct));

    /// <summary>Step 2 of closing: enter counted cash, variance is calculated, shift becomes Closed.</summary>
    [HttpPost("close")]
    public async Task<ActionResult<ShiftDto>> Close(CloseShiftRequest request, CancellationToken ct) =>
        Ok(await shifts.CloseAsync(request.ClosingCash, ct));
}
