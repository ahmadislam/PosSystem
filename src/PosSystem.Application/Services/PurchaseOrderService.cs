using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;

namespace PosSystem.Application.Services;

public interface IPurchaseOrderService
{
    Task<PagedResult<PurchaseOrderDto>> ListAsync(PurchaseOrderStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<PurchaseOrderDto> GetAsync(int id, CancellationToken ct = default);
    Task<PurchaseOrderDto> CreateAsync(PurchaseOrderRequest request, CancellationToken ct = default);
    Task<PurchaseOrderDto> UpdateDraftAsync(int id, PurchaseOrderRequest request, CancellationToken ct = default);
    Task<PurchaseOrderDto> OrderAsync(int id, CancellationToken ct = default);
    Task<PurchaseOrderDto> ReceiveAsync(int id, CancellationToken ct = default);
    Task<PurchaseOrderDto> CancelAsync(int id, CancellationToken ct = default);
}

public class PurchaseOrderService(IAppDbContext db, ICurrentUser current, ILowStockNotifier lowStock) : IPurchaseOrderService
{
    public async Task<PagedResult<PurchaseOrderDto>> ListAsync(
        PurchaseOrderStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.PurchaseOrders.AsNoTracking().IgnoreQueryFilters()
            .Include(p => p.Supplier).Include(p => p.Items).ThenInclude(i => i.Product).AsQueryable();
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        var count = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<PurchaseOrderDto>(orders.Select(ToDto).ToList(), page, pageSize, count);
    }

    public async Task<PurchaseOrderDto> GetAsync(int id, CancellationToken ct = default) =>
        ToDto(await LoadAsync(id, ct));

    public async Task<PurchaseOrderDto> CreateAsync(PurchaseOrderRequest request, CancellationToken ct = default)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        var supplier = await GetActiveSupplierAsync(request.SupplierId, ct);
        var products = await ValidateItemsAsync(request.Items, ct);
        var order = new PurchaseOrder
        {
            SupplierId = supplier.Id,
            CreatedByUserId = userId,
            Status = PurchaseOrderStatus.Draft,
            Items = request.Items.Select(line => new PurchaseOrderItem
            {
                ProductId = line.ProductId,
                Product = products[line.ProductId],
                Quantity = line.Quantity,
                UnitCost = line.UnitCost
            }).ToList()
        };
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(ct);
        return await GetAsync(order.Id, ct);
    }

    public async Task<PurchaseOrderDto> UpdateDraftAsync(int id, PurchaseOrderRequest request, CancellationToken ct = default)
    {
        var order = await LoadAsync(id, ct);
        EnsureStatus(order, PurchaseOrderStatus.Draft, "Only draft purchase orders can be edited.");
        var supplier = await GetActiveSupplierAsync(request.SupplierId, ct);
        var products = await ValidateItemsAsync(request.Items, ct);
        order.SupplierId = supplier.Id;
        db.PurchaseOrderItems.RemoveRange(order.Items);
        order.Items.Clear();
        foreach (var line in request.Items)
            order.Items.Add(new PurchaseOrderItem
            {
                ProductId = line.ProductId,
                Product = products[line.ProductId],
                Quantity = line.Quantity,
                UnitCost = line.UnitCost
            });
        order.Version++;
        await SaveWithConcurrencyCheck(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PurchaseOrderDto> OrderAsync(int id, CancellationToken ct = default)
    {
        var order = await LoadAsync(id, ct);
        EnsureStatus(order, PurchaseOrderStatus.Draft, "Only draft purchase orders can be ordered.");
        if (order.Items.Count == 0) throw new BadRequestException("A purchase order must contain at least one item.");
        order.Status = PurchaseOrderStatus.Ordered;
        order.OrderedAt = DateTime.UtcNow;
        order.Version++;
        await SaveWithConcurrencyCheck(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PurchaseOrderDto> ReceiveAsync(int id, CancellationToken ct = default)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = await LoadAsync(id, ct);
            EnsureStatus(order, PurchaseOrderStatus.Ordered, "Only ordered purchase orders can be received.");
            var productIds = order.Items.Select(i => i.ProductId).Distinct().ToArray();
            var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            if (products.Count != productIds.Length)
                throw new ConflictException("A product on this purchase order is no longer available.");

            foreach (var item in order.Items)
            {
                var product = products[item.ProductId];
                if (product.StockQuantity > int.MaxValue - item.Quantity)
                    throw new ConflictException($"Receiving {product.Sku} would exceed the supported stock quantity.");
                product.StockQuantity += item.Quantity;
                db.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    CreatedByUserId = userId,
                    Type = StockMovementType.Purchase,
                    QuantityChange = item.Quantity,
                    ReferenceType = "PurchaseOrder",
                    ReferenceId = order.Id,
                    Note = $"Received purchase order #{order.Id}"
                });
            }
            await lowStock.CheckAsync(productIds, ct);
            order.Status = PurchaseOrderStatus.Received;
            order.ReceivedAt = DateTime.UtcNow;
            order.Version++;
            await SaveWithConcurrencyCheck(ct);
            await transaction.CommitAsync(ct);
            return await GetAsync(id, ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<PurchaseOrderDto> CancelAsync(int id, CancellationToken ct = default)
    {
        var order = await LoadAsync(id, ct);
        if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered))
            throw new ConflictException("Only draft or ordered purchase orders can be cancelled.");
        order.Status = PurchaseOrderStatus.Cancelled;
        order.Version++;
        await SaveWithConcurrencyCheck(ct);
        return await GetAsync(id, ct);
    }

    private async Task SaveWithConcurrencyCheck(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This purchase order changed while you were working. Refresh and try again.");
        }
    }

    private async Task<PurchaseOrder> LoadAsync(int id, CancellationToken ct) =>
        await db.PurchaseOrders.IgnoreQueryFilters().Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new NotFoundException("Purchase order not found.");

    private async Task<Supplier> GetActiveSupplierAsync(int id, CancellationToken ct) =>
        await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && s.IsActive, ct)
        ?? throw new BadRequestException("Supplier does not exist or is inactive.");

    private async Task<Dictionary<int, Product>> ValidateItemsAsync(
        IReadOnlyCollection<PurchaseOrderLineRequest> lines, CancellationToken ct)
    {
        if (lines.Count == 0) throw new BadRequestException("A purchase order needs at least one item.");
        if (lines.Select(i => i.ProductId).Distinct().Count() != lines.Count)
            throw new BadRequestException("Each product may appear once in a purchase order.");
        if (lines.Any(i => i.Quantity <= 0 || i.UnitCost < 0))
            throw new BadRequestException("Purchase quantities must be positive and unit costs cannot be negative.");

        var productIds = lines.Select(i => i.ProductId).ToArray();
        var products = await db.Products.Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != productIds.Length)
            throw new BadRequestException("One or more products do not exist or are inactive.");
        return products;
    }

    private static void EnsureStatus(PurchaseOrder order, PurchaseOrderStatus expected, string message)
    {
        if (order.Status != expected) throw new ConflictException(message);
    }

    private static PurchaseOrderDto ToDto(PurchaseOrder order)
    {
        var items = order.Items.OrderBy(i => i.Id).Select(i =>
            new PurchaseOrderItemDto(i.Id, i.ProductId, i.Product.Sku, i.Product.Name,
                i.Quantity, i.UnitCost, i.Quantity * i.UnitCost)).ToList();
        return new PurchaseOrderDto(order.Id, order.SupplierId, order.Supplier.Name, order.CreatedByUserId,
            order.Status, order.CreatedAt, order.OrderedAt, order.ReceivedAt,
            items.Sum(i => i.LineTotal), items);
    }
}
