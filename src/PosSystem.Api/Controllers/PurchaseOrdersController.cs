using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Enums;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/purchase-orders"), Authorize(Roles = "Admin,Manager")]
public class PurchaseOrdersController(IPurchaseOrderService purchaseOrders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseOrderDto>>> List(
        [FromQuery] PurchaseOrderStatus? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await purchaseOrders.ListAsync(status, page, pageSize, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PurchaseOrderDto>> Get(int id, CancellationToken ct) =>
        Ok(await purchaseOrders.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<PurchaseOrderDto>> Create(PurchaseOrderRequest request, CancellationToken ct)
    {
        var order = await purchaseOrders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PurchaseOrderDto>> UpdateDraft(int id, PurchaseOrderRequest request, CancellationToken ct) =>
        Ok(await purchaseOrders.UpdateDraftAsync(id, request, ct));

    [HttpPost("{id:int}/order")]
    public async Task<ActionResult<PurchaseOrderDto>> Order(int id, CancellationToken ct) =>
        Ok(await purchaseOrders.OrderAsync(id, ct));

    [HttpPost("{id:int}/receive")]
    public async Task<ActionResult<PurchaseOrderDto>> Receive(int id, CancellationToken ct) =>
        Ok(await purchaseOrders.ReceiveAsync(id, ct));

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<PurchaseOrderDto>> Cancel(int id, CancellationToken ct) =>
        Ok(await purchaseOrders.CancelAsync(id, ct));
}
