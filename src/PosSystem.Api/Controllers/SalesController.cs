using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/sales"), Authorize]
public class SalesController(ISaleService sales, ISaleReturnService returns) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SaleDto>> Create(CreateSaleRequest request, CancellationToken ct)
    {
        var dto = await sales.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SaleDto>> Get(int id, CancellationToken ct) => Ok(await sales.GetAsync(id, ct));

    [HttpPost("{saleId:int}/returns")]
    public async Task<ActionResult<SaleReturnDto>> Return(int saleId, CreateSaleReturnRequest request, CancellationToken ct) =>
        Ok(await returns.CreateAsync(saleId, request, ct));

    [HttpGet("{saleId:int}/returns")]
    public async Task<ActionResult<IReadOnlyList<SaleReturnDto>>> ReturnHistory(int saleId, CancellationToken ct) =>
        Ok(await returns.ListAsync(saleId, ct));

    /// <param name="from">Start date (yyyy-MM-dd), inclusive.</param>
    /// <param name="to">End date (yyyy-MM-dd), inclusive.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<SaleDto>>> List([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sales.ListAsync(from, to, page, pageSize, ct));
}
