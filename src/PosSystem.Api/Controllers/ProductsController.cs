using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/products"), Authorize]
public class ProductsController(IProductService products, ProductImageStorage images) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> Search([FromQuery] string? search, [FromQuery] int? categoryId,
        [FromQuery] bool lowStockOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await products.SearchAsync(search, categoryId, lowStockOnly, page, pageSize, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> Get(int id, CancellationToken ct) => Ok(await products.GetAsync(id, ct));

    [HttpGet("by-sku/{sku}")]
    public async Task<ActionResult<ProductDto>> GetBySku(string sku, CancellationToken ct) =>
        Ok(await products.GetBySkuAsync(sku, ct));

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

    [HttpPost("{id:int}/images"), Authorize(Roles = "Admin,Manager"), RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file, CancellationToken ct)
    {
        var image = await images.SaveAsync(id, file, ct);
        return CreatedAtAction(nameof(GetImage), new { id, imageId = image.Id },
            new { image.Id, image.ContentType, image.IsPrimary });
    }

    [HttpGet("{id:int}/images"), Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<IReadOnlyList<ProductImageDto>>> ListImages(int id, CancellationToken ct) =>
        Ok(await images.ListAsync(id, ct));

    [HttpPut("{id:int}/images/{imageId:int}/primary"), Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> SetPrimaryImage(int id, int imageId, CancellationToken ct)
    {
        await images.SetPrimaryAsync(id, imageId, ct);
        return NoContent();
    }

    [HttpGet("{id:int}/images/{imageId:int}"), AllowAnonymous]
    public async Task<IActionResult> GetImage(int id, int imageId, CancellationToken ct)
    {
        var (image, path) = await images.GetAsync(id, imageId, ct);
        return PhysicalFile(path, image.ContentType, enableRangeProcessing: true);
    }

    [HttpDelete("{id:int}/images/{imageId:int}"), Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeleteImage(int id, int imageId, CancellationToken ct)
    {
        await images.DeleteAsync(id, imageId, ct);
        return NoContent();
    }
}
