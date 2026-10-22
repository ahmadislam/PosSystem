using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/categories"), Authorize]
public class CategoriesController(ICategoryService categories) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken ct) => Ok(await categories.ListAsync(ct));

    [HttpPost, Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest request, CancellationToken ct) =>
        StatusCode(201, await categories.CreateAsync(request, ct));

    [HttpPut("{id:int}"), Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CategoryDto>> Update(int id, CategoryRequest request, CancellationToken ct) =>
        Ok(await categories.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}"), Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}
