using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/suppliers"), Authorize(Roles = "Admin,Manager")]
public class SuppliersController(ISupplierService suppliers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> List(CancellationToken ct) =>
        Ok(await suppliers.ListAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SupplierDto>> Get(int id, CancellationToken ct) =>
        Ok(await suppliers.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(SupplierRequest request, CancellationToken ct)
    {
        var supplier = await suppliers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = supplier.Id }, supplier);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SupplierDto>> Update(int id, SupplierRequest request, CancellationToken ct) =>
        Ok(await suppliers.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await suppliers.DeleteAsync(id, ct);
        return NoContent();
    }
}
