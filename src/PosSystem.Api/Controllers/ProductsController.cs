using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/products"), Authorize]
public class ProductsController(IProductService products) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> Search([FromQuery] string? search, [FromQuery] int? categoryId,
        [FromQuery] bool lowStockOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await products.SearchAsync(search, categoryId, lowStockOnly, page, pageSize, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> Get(int id, CancellationToken ct) => Ok(await products.GetAsync(id, ct));

    [HttpPost, Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ProductDto>> Create(ProductRequest request, CancellationToken ct)
    {
        var dto = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:int}"), Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ProductDto>> Update(int id, ProductRequest request, CancellationToken ct) =>
        Ok(await products.UpdateAsync(id, request, ct));

    [HttpPost("{id:int}/adjust-stock"), Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ProductDto>> AdjustStock(int id, StockAdjustRequest request, CancellationToken ct) =>
        Ok(await products.AdjustStockAsync(id, request, ct));

    [HttpDelete("{id:int}"), Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }
}
