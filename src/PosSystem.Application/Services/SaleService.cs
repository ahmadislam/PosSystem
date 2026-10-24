using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;
using PosSystem.Domain.Services;

namespace PosSystem.Application.Services;

public interface ISaleService
{
    Task<SaleDto> CreateAsync(CreateSaleRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<SaleDto> GetAsync(int id, CancellationToken ct = default);
    Task<PagedResult<SaleDto>> ListAsync(DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);
}

public class SaleService(
    IAppDbContext db,
    ICurrentUser current,
    ILowStockNotifier lowStock) : ISaleService
{
    public async Task<SaleDto> CreateAsync(CreateSaleRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        if (request.Items.Count == 0) throw new BadRequestException("A sale needs at least one item.");
        if (!Enum.IsDefined(request.PaymentMethod)) throw new BadRequestException("Payment method is invalid.");
        if (request.PaymentMethod == PaymentMethod.Cash)
        {
            if (!request.CashTendered.HasValue || request.CashTendered < 0 || request.CashTendered > 1_000_000_000m
                || decimal.Round(request.CashTendered.Value, 2) != request.CashTendered)
                throw new BadRequestException("Cash tendered is required and must be a nonnegative amount with at most two decimal places.");
        }
        else if (request.CashTendered.HasValue)
            throw new BadRequestException("Cash tendered applies only to cash payments.");
        if (request.PaymentMethod == PaymentMethod.Cash && (!string.IsNullOrWhiteSpace(request.PaymentProvider) || !string.IsNullOrWhiteSpace(request.PaymentReference)))
            throw new BadRequestException("Payment provider and reference apply only to card or online payments.");
        if (request.PaymentMethod != PaymentMethod.Cash && string.IsNullOrWhiteSpace(request.PaymentReference))
            throw new BadRequestException("A payment reference is required for card and online payments.");
        if (request.LoyaltyPointsToRedeem < 0) throw new BadRequestException("Loyalty points to redeem cannot be negative.");
        if (!Guid.TryParse(idempotencyKey, out var parsedKey))
            throw new BadRequestException("A valid Idempotency-Key UUID header is required.");
        idempotencyKey = parsedKey.ToString("N");
        var requestHash = HashRequest(request);

        var ids = request.Items.Select(i => i.ProductId).Distinct().ToList();
        if (ids.Count != request.Items.Count)
            throw new BadRequestException("Each product may appear once; increase the quantity instead.");

        var previousSale = await db.Sales.AsNoTracking().Include(s => s.Items).Include(s => s.Customer).Include(s => s.Coupon)
            .SingleOrDefaultAsync(s => s.CashierId == userId && s.IdempotencyKey == idempotencyKey, ct);
        if (previousSale is not null)
            return ReturnIfSameRequest(previousSale, requestHash);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var shiftId = await db.Database
                .SqlQuery<int>($"SELECT \"Id\" AS \"Value\" FROM \"CashierShifts\" WHERE \"CashierId\" = {userId} AND \"Status\" = {(int)ShiftStatus.Open} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (shiftId == 0) throw new BadRequestException("Open a shift before making sales.");

            var shift = await db.CashierShifts.FirstAsync(s => s.Id == shiftId, ct);
            previousSale = await db.Sales.AsNoTracking().Include(s => s.Items).Include(s => s.Customer).Include(s => s.Coupon)
                .SingleOrDefaultAsync(s => s.CashierId == userId && s.IdempotencyKey == idempotencyKey, ct);
            if (previousSale is not null)
            {
                var result = ReturnIfSameRequest(previousSale, requestHash);
                await tx.CommitAsync(ct);
                return result;
            }

            var products = await db.Products.Where(p => ids.Contains(p.Id) && p.IsActive).ToDictionaryAsync(p => p.Id, ct);
                Customer? customer = null;
                if (request.CustomerId.HasValue)
                {
                    var customerId = await db.Database.SqlQuery<int>(
                        $"SELECT \"Id\" AS \"Value\" FROM \"Customers\" WHERE \"Id\" = {request.CustomerId.Value} AND \"IsActive\" = TRUE AND \"IsDeleted\" = FALSE FOR UPDATE")
                        .SingleOrDefaultAsync(ct);
                    if (customerId == 0) throw new BadRequestException("Customer does not exist or is inactive.");
                    customer = await db.Customers.FirstAsync(c => c.Id == customerId, ct);
                }

                Coupon? coupon = null;
                if (!string.IsNullOrWhiteSpace(request.CouponCode))
                {
                    var code = request.CouponCode.Trim().ToUpperInvariant();
                    var couponId = await db.Database.SqlQuery<int>(
                        $"SELECT \"Id\" AS \"Value\" FROM \"Coupons\" WHERE \"Code\" = {code} AND \"IsActive\" = TRUE FOR UPDATE")
                        .SingleOrDefaultAsync(ct);
                    if (couponId == 0) throw new BadRequestException("Coupon code is invalid or inactive.");
                    coupon = await db.Coupons.FirstAsync(c => c.Id == couponId, ct);
                    var now = DateTime.UtcNow;
                    if (now < coupon.StartsAt || now >= coupon.EndsAt)
                        throw new BadRequestException("Coupon is outside its validity period.");
                    if (coupon.MaxRedemptions.HasValue && coupon.RedemptionCount >= coupon.MaxRedemptions.Value)
                        throw new ConflictException("Coupon redemption limit has been reached.");
                }

                decimal eligibleSubtotal = 0;
                foreach (var line in request.Items)
                {
                    if (line.Quantity <= 0) throw new BadRequestException("Quantity must be positive.");
                    if (line.DiscountAmount < 0 || decimal.Round(line.DiscountAmount, 2) != line.DiscountAmount)
                        throw new BadRequestException("Line discounts must be nonnegative amounts with at most two decimal places.");
                    if (!products.TryGetValue(line.ProductId, out var product))
                        throw new NotFoundException($"Product {line.ProductId} not found or inactive.");
                    if (product.StockQuantity < line.Quantity)
                        throw new ConflictException($"Insufficient stock for {product.Sku}: {product.StockQuantity} left.");
                    var lineBase = product.SalePrice * line.Quantity;
                    if (line.DiscountAmount > lineBase)
                        throw new BadRequestException($"Discount cannot exceed the line amount for {product.Sku}.");
                    eligibleSubtotal += lineBase - line.DiscountAmount;
                }

                var couponDiscount = coupon is null ? 0m : coupon.IsPercentage
                    ? Math.Round(eligibleSubtotal * coupon.Value / 100m, 2, MidpointRounding.AwayFromZero)
                    : Math.Min(eligibleSubtotal, coupon.Value);
                var loyaltyValues = await db.Settings.AsNoTracking().Where(setting =>
                        setting.SettingKey == "LoyaltyEarnPointsPerCurrency" ||
                        setting.SettingKey == "LoyaltyRedemptionPointsPerCurrency")
                    .ToDictionaryAsync(setting => setting.SettingKey, setting => setting.Value, ct);
                var earnPointsRate = ReadLoyaltyRule(loyaltyValues, "LoyaltyEarnPointsPerCurrency", 1);
                var redemptionRate = ReadLoyaltyRule(loyaltyValues, "LoyaltyRedemptionPointsPerCurrency", 100);
                var redemptionDiscount = request.LoyaltyPointsToRedeem / (decimal)redemptionRate;
                if (request.LoyaltyPointsToRedeem > 0)
                {
                    if (customer is null) throw new BadRequestException("Select a customer to redeem loyalty points.");
                    if (request.LoyaltyPointsToRedeem % redemptionRate != 0)
                        throw new BadRequestException($"Loyalty redemption must use multiples of {redemptionRate} points.");
                    redemptionDiscount = Math.Round(redemptionDiscount, 2, MidpointRounding.AwayFromZero);
                    if (customer.LoyaltyPoints < request.LoyaltyPointsToRedeem)
                        throw new BadRequestException("Customer does not have enough loyalty points.");
                    if (redemptionDiscount > eligibleSubtotal - couponDiscount)
                        throw new BadRequestException("Loyalty redemption cannot exceed the remaining eligible subtotal.");
                }
                var additionalDiscount = couponDiscount + redemptionDiscount;
                var additionalDiscounts = DiscountAllocator.Allocate(additionalDiscount,
                    request.Items.Select(line => products[line.ProductId].SalePrice * line.Quantity - line.DiscountAmount).ToArray());
                var sale = new Sale
                {
                    CashierId = userId, ShiftId = shift.Id, SaleDate = DateTime.UtcNow,
                    PaymentMethod = request.PaymentMethod, Status = SaleStatus.Completed,
                    PaymentProvider = request.PaymentProvider?.Trim(), PaymentReference = request.PaymentReference?.Trim(),
                    PaymentStatus = request.PaymentMethod == PaymentMethod.Cash ? "Completed" : "Approved",
                    IdempotencyKey = idempotencyKey, RequestHash = requestHash,
                    CustomerId = customer?.Id, Customer = customer, CouponId = coupon?.Id, Coupon = coupon,
                    LoyaltyPointsRedeemed = request.LoyaltyPointsToRedeem
                };
                var loyaltyEntries = new List<LoyaltyTransaction>();

                for (var index = 0; index < request.Items.Count; index++)
                {
                    var line = request.Items[index];
                    var product = products[line.ProductId];
                    var discount = line.DiscountAmount + additionalDiscounts[index];

                    var (gross, tax, total) = PricingCalculator.CalculateLine(product.SalePrice, line.Quantity, discount, product.TaxRate);
                    product.StockQuantity -= line.Quantity;   // RowVersion (xmin) guards against concurrent sales

                    sale.Items.Add(new SaleItem
                    {
                        ProductId = product.Id, Sku = product.Sku, ProductName = product.Name, Quantity = line.Quantity,
                        UnitPrice = product.SalePrice, DiscountAmount = discount, TaxAmount = tax, LineTotal = total
                    });
                    sale.Subtotal += gross;
                    sale.DiscountAmount += discount;
                sale.TaxAmount += tax;
                sale.TotalAmount += total;
            }

            if (request.PaymentMethod == PaymentMethod.Cash)
            {
                if (request.CashTendered!.Value < sale.TotalAmount)
                    throw new BadRequestException($"Cash tendered must cover the current sale total of {sale.TotalAmount:0.00}.");
                sale.CashTendered = request.CashTendered;
                sale.ChangeDue = request.CashTendered.Value - sale.TotalAmount;
            }

            if (coupon is not null) coupon.RedemptionCount++;
            if (customer is not null)
            {
                if (request.LoyaltyPointsToRedeem > 0)
                {
                    customer.LoyaltyPoints = checked(customer.LoyaltyPoints - request.LoyaltyPointsToRedeem);
                    loyaltyEntries.Add(new LoyaltyTransaction
                    {
                        CustomerId = customer.Id, PointsChange = -request.LoyaltyPointsToRedeem,
                        BalanceAfter = customer.LoyaltyPoints, Type = "Redeem", Note = "Points redeemed at checkout"
                    });
                }
                var pointsEarned = checked((int)Math.Floor(Math.Max(0, sale.Subtotal - sale.DiscountAmount) * earnPointsRate));
                sale.LoyaltyPointsEarned = pointsEarned;
                if (pointsEarned > 0)
                {
                    customer.LoyaltyPoints = checked(customer.LoyaltyPoints + pointsEarned);
                    loyaltyEntries.Add(new LoyaltyTransaction
                    {
                        CustomerId = customer.Id, PointsChange = pointsEarned,
                        BalanceAfter = customer.LoyaltyPoints, Type = "Earn", Note = "Points earned at checkout"
                    });
                }
            }

            await lowStock.CheckAsync(ids, ct);

            // Database sequence: unique and race-free even with several cashiers.
            var seq = await db.Database.SqlQuery<int>($"SELECT nextval('invoice_seq')::int AS \"Value\"").SingleAsync(ct);
            sale.InvoiceNumber = $"INV-{sale.SaleDate:yyyyMMdd}-{seq:D6}";

            db.Sales.Add(sale);
            await db.SaveChangesAsync(ct);   // inserts sale + items, updates stock (concurrency checked)

            foreach (var loyaltyEntry in loyaltyEntries) loyaltyEntry.SaleId = sale.Id;
            db.LoyaltyTransactions.AddRange(loyaltyEntries);
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
            return await GetAsync(sale.Id, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Stock changed while the sale was processing. Please retry.");
        }
    }

    private static SaleDto ReturnIfSameRequest(Sale sale, string requestHash)
    {
        if (!string.Equals(sale.RequestHash, requestHash, StringComparison.Ordinal))
            throw new ConflictException("This Idempotency-Key was already used with a different checkout request.");
        return ToDto(sale);
    }

    private static string HashRequest(CreateSaleRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.PaymentMethod,
            request.CashTendered,
            request.CustomerId,
            CouponCode = request.CouponCode?.Trim().ToUpperInvariant(),
            request.LoyaltyPointsToRedeem,
            Items = request.Items.OrderBy(item => item.ProductId)
                .Select(item => new { item.ProductId, item.Quantity, item.DiscountAmount })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static int ReadLoyaltyRule(IReadOnlyDictionary<string, string> settings, string key, int fallback)
    {
        if (!settings.TryGetValue(key, out var value)) return fallback;
        if (int.TryParse(value, out var parsed) && parsed > 0) return parsed;
        throw new InvalidOperationException($"The loyalty rule {key} is not a positive integer.");
    }

    public async Task<SaleDto> GetAsync(int id, CancellationToken ct = default)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Items).Include(s => s.Customer).Include(s => s.Coupon)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Sale not found.");
        EnsureCanAccess(sale);
        return ToDto(sale);
    }

    public async Task<PagedResult<SaleDto>> ListAsync(DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Sales.AsNoTracking().Include(s => s.Items).Include(s => s.Customer).Include(s => s.Coupon).AsQueryable();
        if (current.Role is not ("Admin" or "Manager"))
        {
            var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
            if (current.Role != "Cashier") throw new ForbiddenException("You cannot view sales.");
            query = query.Where(s => s.CashierId == userId);
        }
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

    private void EnsureCanAccess(Sale sale)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        if (current.Role is "Admin" or "Manager" || current.Role == "Cashier" && sale.CashierId == userId) return;
        throw new ForbiddenException("You can only view sales that you processed.");
    }

    private static SaleDto ToDto(Sale s) => new(s.Id, s.InvoiceNumber, s.SaleDate, s.Subtotal, s.DiscountAmount,
        s.TaxAmount, s.TotalAmount, s.PaymentMethod, s.Status,
        s.Items.Select(i => new SaleItemDto(i.Id, i.ProductId, i.Sku, i.ProductName, i.Quantity, i.UnitPrice,
        i.DiscountAmount, i.TaxAmount, i.LineTotal)).ToList(), s.CustomerId, s.Customer?.Name, s.Coupon?.Code,
        s.LoyaltyPointsEarned, s.LoyaltyPointsRedeemed, s.CashTendered, s.ChangeDue,
        s.PaymentProvider, s.PaymentReference, s.PaymentStatus);
}
