using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;
using PosSystem.Domain.Services;

namespace PosSystem.Application.Services;

public interface ISaleService
{
    Task<SaleDto> CreateAsync(CreateSaleRequest request, CancellationToken ct = default);
    Task<SaleDto> GetAsync(int id, CancellationToken ct = default);
    Task<PagedResult<SaleDto>> ListAsync(DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);
}

public class SaleService(IAppDbContext db, ICurrentUser current, ILowStockNotifier lowStock) : ISaleService
{
    public async Task<SaleDto> CreateAsync(CreateSaleRequest request, CancellationToken ct = default)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        if (request.Items.Count == 0) throw new BadRequestException("A sale needs at least one item.");

        var ids = request.Items.Select(i => i.ProductId).Distinct().ToList();
        if (ids.Count != request.Items.Count)
            throw new BadRequestException("Each product may appear once; increase the quantity instead.");

        var shift = await db.CashierShifts.FirstOrDefaultAsync(s => s.CashierId == userId && s.Status == ShiftStatus.Open, ct)
            ?? throw new BadRequestException("Open a shift before making sales.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var products = await db.Products.Where(p => ids.Contains(p.Id) && p.IsActive).ToDictionaryAsync(p => p.Id, ct);
            var sale = new Sale
            {
                CashierId = userId, ShiftId = shift.Id, SaleDate = DateTime.UtcNow,
                PaymentMethod = request.PaymentMethod, Status = SaleStatus.Completed
            };

            foreach (var line in request.Items)
            {
                if (!products.TryGetValue(line.ProductId, out var product))
                    throw new NotFoundException($"Product {line.ProductId} not found or inactive.");
                if (product.StockQuantity < line.Quantity)
                    throw new ConflictException($"Insufficient stock for {product.Sku}: {product.StockQuantity} left.");

                var (gross, tax, total) = PricingCalculator.CalculateLine(product.SalePrice, line.Quantity, line.DiscountAmount, product.TaxRate);
                product.StockQuantity -= line.Quantity;   // RowVersion (xmin) guards against concurrent sales

                sale.Items.Add(new SaleItem
                {
                    ProductId = product.Id, Sku = product.Sku, ProductName = product.Name, Quantity = line.Quantity,
                    UnitPrice = product.SalePrice, DiscountAmount = line.DiscountAmount, TaxAmount = tax, LineTotal = total
                });
                sale.Subtotal += gross;
                sale.DiscountAmount += line.DiscountAmount;
                sale.TaxAmount += tax;
                sale.TotalAmount += total;
            }

            // Database sequence: unique and race-free even with several cashiers.
            var seq = await db.Database.SqlQuery<int>($"SELECT nextval('invoice_seq')::int AS \"Value\"").SingleAsync(ct);
            sale.InvoiceNumber = $"INV-{sale.SaleDate:yyyyMMdd}-{seq:D6}";

            db.Sales.Add(sale);
            await db.SaveChangesAsync(ct);   // inserts sale + items, updates stock (concurrency checked)

            foreach (var item in sale.Items)
            {
                db.StockMovements.Add(new StockMovement
                {
                    ProductId = item.ProductId, CreatedByUserId = userId, Type = StockMovementType.Sale,
                    QuantityChange = -item.Quantity, ReferenceType = "Sale", ReferenceId = sale.Id, Note = sale.InvoiceNumber
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await lowStock.CheckAsync(ids, ct);   // only after the commit succeeded
            return await GetAsync(sale.Id, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Stock changed while the sale was processing. Please retry.");
        }
    }

    public async Task<SaleDto> GetAsync(int id, CancellationToken ct = default)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Sale not found.");
        return ToDto(sale);
    }

    public async Task<PagedResult<SaleDto>> ListAsync(DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Sales.AsNoTracking().Include(s => s.Items).AsQueryable();
        if (from.HasValue)
        {
            var f = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(s => s.SaleDate >= f);
        }
        if (to.HasValue)
        {
            var t = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);   // 'to' date is inclusive
            query = query.Where(s => s.SaleDate < t);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(s => s.SaleDate).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<SaleDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    private static SaleDto ToDto(Sale s) => new(s.Id, s.InvoiceNumber, s.SaleDate, s.Subtotal, s.DiscountAmount,
        s.TaxAmount, s.TotalAmount, s.PaymentMethod, s.Status,
        s.Items.Select(i => new SaleItemDto(i.ProductId, i.Sku, i.ProductName, i.Quantity, i.UnitPrice,
            i.DiscountAmount, i.TaxAmount, i.LineTotal)).ToList());
}
