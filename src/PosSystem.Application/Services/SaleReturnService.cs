using System.Data;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;
using PosSystem.Domain.Services;

namespace PosSystem.Application.Services;

public interface ISaleReturnService
{
    Task<SaleReturnDto> CreateAsync(int saleId, CreateSaleReturnRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SaleReturnDto>> ListAsync(int saleId, CancellationToken ct = default);
}

public class SaleReturnService(IAppDbContext db, ICurrentUser current, ILowStockNotifier lowStock) : ISaleReturnService
{
    public async Task<SaleReturnDto> CreateAsync(int saleId, CreateSaleReturnRequest request, CancellationToken ct = default)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        if (request.Items.Count == 0) throw new BadRequestException("Select at least one item to return.");
        if (!Enum.IsDefined(request.RefundMethod)) throw new BadRequestException("Refund method is invalid.");

        var requestedIds = request.Items.Select(i => i.SaleItemId).ToArray();
        if (requestedIds.Distinct().Count() != requestedIds.Length)
            throw new BadRequestException("Each sale line may appear once in a return.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var lockedSaleId = await db.Database
            .SqlQuery<int>($"SELECT \"Id\" AS \"Value\" FROM \"Sales\" WHERE \"Id\" = {saleId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (lockedSaleId == 0) throw new NotFoundException("Sale not found.");

        var sale = await db.Sales.Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new NotFoundException("Sale not found.");
        EnsureCanAccess(sale, userId);
        if (sale.Status == SaleStatus.Refunded) throw new ConflictException("This sale has already been fully returned.");

        var shiftId = await db.Database
            .SqlQuery<int>($"SELECT \"Id\" AS \"Value\" FROM \"CashierShifts\" WHERE \"CashierId\" = {userId} AND \"Status\" = {(int)ShiftStatus.Open} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (shiftId == 0) throw new BadRequestException("Open a shift before processing a return.");
        var shift = await db.CashierShifts.FirstAsync(s => s.Id == shiftId, ct);

        var saleItems = sale.Items.ToDictionary(i => i.Id);
        var unknownIds = requestedIds.Where(id => !saleItems.ContainsKey(id)).ToArray();
        if (unknownIds.Length > 0) throw new BadRequestException("One or more return lines do not belong to this sale.");

        var saleItemIds = sale.Items.Select(i => i.Id).ToArray();
        var previousItems = await db.SaleReturnItems.AsNoTracking()
            .Where(i => saleItemIds.Contains(i.SaleItemId))
            .GroupBy(i => i.SaleItemId)
            .Select(g => new
            {
                SaleItemId = g.Key,
                Quantity = g.Sum(i => i.Quantity),
                Subtotal = g.Sum(i => i.Subtotal),
                Discount = g.Sum(i => i.DiscountAmount),
                Tax = g.Sum(i => i.TaxAmount),
                Total = g.Sum(i => i.LineTotal)
            })
            .ToDictionaryAsync(x => x.SaleItemId, ct);

        var productIds = request.Items.Select(i => saleItems[i.SaleItemId].ProductId).Distinct().ToArray();
        var products = await db.Products.IgnoreQueryFilters()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        var saleReturn = new SaleReturn
        {
            SaleId = sale.Id,
            ShiftId = shift.Id,
            ProcessedByUserId = userId,
            RefundDate = DateTime.UtcNow,
            RefundMethod = request.RefundMethod,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
        };

        foreach (var requested in request.Items)
        {
            var original = saleItems[requested.SaleItemId];
            previousItems.TryGetValue(original.Id, out var previous);
            var returnedQuantity = previous?.Quantity ?? 0;
            if (requested.Quantity > original.Quantity - returnedQuantity)
                throw new ConflictException($"Only {original.Quantity - returnedQuantity} of {original.ProductName} can still be returned.");

            if (!products.TryGetValue(original.ProductId, out var product))
                throw new NotFoundException($"Product {original.Sku} no longer exists.");
            if (product.StockQuantity > int.MaxValue - requested.Quantity)
                throw new ConflictException($"Returning {original.Sku} would exceed the supported stock quantity.");

            var amounts = ReturnCalculator.Calculate(
                original.UnitPrice,
                original.Quantity,
                original.DiscountAmount,
                original.TaxAmount,
                original.LineTotal,
                requested.Quantity,
                returnedQuantity,
                previous?.Subtotal ?? 0m,
                previous?.Discount ?? 0m,
                previous?.Tax ?? 0m,
                previous?.Total ?? 0m);

            product.StockQuantity += requested.Quantity;
            saleReturn.Subtotal += amounts.Subtotal;
            saleReturn.DiscountAmount += amounts.DiscountAmount;
            saleReturn.TaxAmount += amounts.TaxAmount;
            saleReturn.TotalAmount += amounts.TotalAmount;
            saleReturn.Items.Add(new SaleReturnItem
            {
                SaleItemId = original.Id,
                ProductId = original.ProductId,
                Sku = original.Sku,
                ProductName = original.ProductName,
                Quantity = requested.Quantity,
                UnitPrice = original.UnitPrice,
                Subtotal = amounts.Subtotal,
                DiscountAmount = amounts.DiscountAmount,
                TaxAmount = amounts.TaxAmount,
                LineTotal = amounts.TotalAmount
            });
        }

        sale.Status = sale.Items.All(item =>
            (previousItems.TryGetValue(item.Id, out var previous) ? previous.Quantity : 0)
            + (request.Items.FirstOrDefault(line => line.SaleItemId == item.Id)?.Quantity ?? 0)
            == item.Quantity)
            ? SaleStatus.Refunded
            : SaleStatus.PartiallyReturned;

        await lowStock.CheckAsync(productIds, ct);
        db.SaleReturns.Add(saleReturn);
        await db.SaveChangesAsync(ct);

        if (sale.CustomerId.HasValue && (sale.LoyaltyPointsEarned > 0 || sale.LoyaltyPointsRedeemed > 0))
        {
            var customerId = await db.Database.SqlQuery<int>(
                $"SELECT \"Id\" AS \"Value\" FROM \"Customers\" WHERE \"Id\" = {sale.CustomerId.Value} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (customerId != 0)
            {
                var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Id == customerId, ct);
                var cumulativeRefund = (previousItems.Values.Sum(value => value.Total) + saleReturn.TotalAmount);
                var cumulativeSubtotal = previousItems.Values.Sum(value => value.Subtotal) + saleReturn.Subtotal;
                // A fully loyalty-paid sale has no cash refund denominator. Use the
                // returned merchandise's pre-discount subtotal so a partial return
                // restores only its proportional points, rather than the whole sale's.
                var refundRatio = sale.TotalAmount > 0
                    ? Math.Min(1m, cumulativeRefund / sale.TotalAmount)
                    : sale.Subtotal > 0
                        ? Math.Min(1m, cumulativeSubtotal / sale.Subtotal)
                        : sale.Status == SaleStatus.Refunded ? 1m : 0m;
                var earnedTarget = (int)Math.Floor(sale.LoyaltyPointsEarned * refundRatio);
                var earnedReversed = await db.LoyaltyTransactions
                    .Where(entry => entry.SaleReturn != null && entry.SaleReturn.SaleId == sale.Id
                        && entry.Type == "ReturnEarnedReversal")
                    .SumAsync(entry => (int?)-entry.PointsChange, ct) ?? 0;
                var earnedDelta = Math.Max(0, earnedTarget - earnedReversed);
                if (earnedDelta > 0)
                {
                    customer.LoyaltyPoints = checked(customer.LoyaltyPoints - earnedDelta);
                    db.LoyaltyTransactions.Add(new LoyaltyTransaction
                    {
                        CustomerId = customer.Id, SaleReturnId = saleReturn.Id,
                        PointsChange = -earnedDelta, BalanceAfter = customer.LoyaltyPoints,
                        Type = "ReturnEarnedReversal", Note = $"Reversed earned points for {sale.InvoiceNumber}"
                    });
                }

                var redeemedTarget = (int)Math.Floor(sale.LoyaltyPointsRedeemed * refundRatio);
                var redeemedRestored = await db.LoyaltyTransactions
                    .Where(entry => entry.SaleReturn != null && entry.SaleReturn.SaleId == sale.Id
                        && entry.Type == "ReturnRedemptionRestore")
                    .SumAsync(entry => (int?)entry.PointsChange, ct) ?? 0;
                var redeemedDelta = Math.Max(0, redeemedTarget - redeemedRestored);
                if (redeemedDelta > 0)
                {
                    customer.LoyaltyPoints = checked(customer.LoyaltyPoints + redeemedDelta);
                    db.LoyaltyTransactions.Add(new LoyaltyTransaction
                    {
                        CustomerId = customer.Id, SaleReturnId = saleReturn.Id,
                        PointsChange = redeemedDelta, BalanceAfter = customer.LoyaltyPoints,
                        Type = "ReturnRedemptionRestore", Note = $"Restored redeemed points for {sale.InvoiceNumber}"
                    });
                }
            }
        }

        foreach (var item in saleReturn.Items)
        {
            db.StockMovements.Add(new StockMovement
            {
                ProductId = item.ProductId,
                CreatedByUserId = userId,
                Type = StockMovementType.Return,
                QuantityChange = item.Quantity,
                ReferenceType = "SaleReturn",
                ReferenceId = saleReturn.Id,
                Note = $"Return for {sale.InvoiceNumber}"
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToDto(saleReturn);
    }

    public async Task<IReadOnlyList<SaleReturnDto>> ListAsync(int saleId, CancellationToken ct = default)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        var sale = await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new NotFoundException("Sale not found.");
        EnsureCanAccess(sale, userId);

        var returns = await db.SaleReturns.AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.SaleId == saleId)
            .OrderByDescending(r => r.RefundDate)
            .ToListAsync(ct);
        return returns.Select(ToDto).ToList();
    }

    private void EnsureCanAccess(Sale sale, int userId)
    {
        if (current.Role is "Admin" or "Manager") return;
        if (current.Role != "Cashier" || sale.CashierId != userId)
            throw new ForbiddenException("You can only view or return sales that you processed.");
    }

    private static SaleReturnDto ToDto(SaleReturn saleReturn) => new(
        saleReturn.Id,
        saleReturn.SaleId,
        saleReturn.ShiftId,
        saleReturn.ProcessedByUserId,
        saleReturn.RefundDate,
        saleReturn.RefundMethod,
        saleReturn.Reason,
        saleReturn.Subtotal,
        saleReturn.DiscountAmount,
        saleReturn.TaxAmount,
        saleReturn.TotalAmount,
        saleReturn.Items.Select(i => new SaleReturnItemDto(
            i.SaleItemId, i.ProductId, i.Sku, i.ProductName, i.Quantity,
            i.UnitPrice, i.Subtotal, i.DiscountAmount, i.TaxAmount, i.LineTotal)).ToList());
}
