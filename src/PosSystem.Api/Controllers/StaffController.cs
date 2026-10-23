using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/staff"), Authorize(Roles = "Admin")]
public sealed class StaffController(IStaffService staff) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StaffDto>>> List(CancellationToken ct) =>
        Ok(await staff.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<StaffDto>> Create(CreateStaffRequest request, CancellationToken ct)
    {
        var created = await staff.CreateAsync(request, ct);
        return CreatedAtAction(nameof(List), created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StaffDto>> Update(int id, UpdateStaffRequest request, CancellationToken ct) =>
        Ok(await staff.UpdateAsync(id, request, ct));

    [HttpPut("{id:int}/password")]
    public async Task<IActionResult> ResetPassword(int id, ResetStaffPasswordRequest request, CancellationToken ct)
    {
        await staff.ResetPasswordAsync(id, request.Password, ct);
        return NoContent();
    }
}
