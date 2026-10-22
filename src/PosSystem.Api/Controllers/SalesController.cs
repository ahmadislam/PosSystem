using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/sales"), Authorize]
public class SalesController(ISaleService sales) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SaleDto>> Create(CreateSaleRequest request, CancellationToken ct)
    {
        var dto = await sales.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SaleDto>> Get(int id, CancellationToken ct) => Ok(await sales.GetAsync(id, ct));

    /// <param name="from">Start date (yyyy-MM-dd), inclusive.</param>
    /// <param name="to">End date (yyyy-MM-dd), inclusive.</param>
    [HttpGet, Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<PagedResult<SaleDto>>> List([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sales.ListAsync(from, to, page, pageSize, ct));
}
