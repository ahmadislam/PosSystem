using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;

namespace PosSystem.Application.Services;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}

public class DashboardService(IAppDbContext db) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var sales = db.Sales.AsNoTracking().AsQueryable();
        if (from.HasValue)
        {
            var start = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            sales = sales.Where(s => s.SaleDate >= start);
        }
        if (to.HasValue)
        {
            var end = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);
            sales = sales.Where(s => s.SaleDate < end);
        }

        var totalSales = await sales.CountAsync(ct);
        var grossSales = await sales.SumAsync(s => (decimal?)s.TotalAmount, ct) ?? 0m;
        var refundQuery = db.SaleReturns.AsNoTracking().AsQueryable();
        if (from.HasValue)
            refundQuery = refundQuery.Where(r => r.RefundDate >= DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc));
        if (to.HasValue)
            refundQuery = refundQuery.Where(r => r.RefundDate < DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc));
        var refunds = await refundQuery.SumAsync(r => (decimal?)r.TotalAmount, ct) ?? 0m;
        var netRevenue = grossSales - refunds;
        var dailyTotals = await sales
            .GroupBy(s => s.SaleDate.Date)
            .Select(g => new { Date = g.Key, GrossSales = g.Sum(s => s.TotalAmount) })
            .OrderBy(d => d.Date)
            .ToListAsync(ct);
        var dailySales = dailyTotals.Select(d => new DailySalesDto(d.Date, d.GrossSales)).ToList();
        var monthlyTotals = await sales
            .GroupBy(s => new { s.SaleDate.Year, s.SaleDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(s => s.TotalAmount) })
            .OrderBy(g => g.Year)
            .ThenBy(g => g.Month)
            .ToListAsync(ct);
        var monthlyRevenue = monthlyTotals
            .Select(g => new MonthlyRevenueDto(new DateTime(g.Year, g.Month, 1, 0, 0, 0, DateTimeKind.Utc), g.Revenue))
            .ToList();

        // Reporting aggregates stay in PostgreSQL rather than materializing sale lines in the API.
        // SqlQuery parameterizes the UTC range; raw SQL must explicitly preserve the Sale soft-delete filter.
        // Historical lines deliberately include retired products/categories, matching invoice snapshots.
        var reportStart = DateTime.SpecifyKind(from?.Date ?? DateTime.MinValue, DateTimeKind.Utc);
        var reportEnd = DateTime.SpecifyKind(to?.Date.AddDays(1) ?? DateTime.MaxValue, DateTimeKind.Utc);
        var topSellingProducts = await db.Database.SqlQuery<TopSellingProductDto>($"""
                SELECT i."Sku", i."ProductName", CAST(SUM(i."Quantity") AS integer) AS "QuantitySold",
                       SUM(i."LineTotal") AS "Revenue"
                FROM "Sales" AS s
                INNER JOIN "SaleItems" AS i ON i."SaleId" = s."Id"
                WHERE s."IsDeleted" = FALSE AND s."SaleDate" >= {reportStart} AND s."SaleDate" < {reportEnd}
                GROUP BY i."Sku", i."ProductName"
                ORDER BY "QuantitySold" DESC, "Revenue" DESC, i."Sku", i."ProductName"
                LIMIT 5
                """)
            .ToListAsync(ct);

        var salesByCategory = await db.Database.SqlQuery<CategorySalesDto>($"""
                SELECT c."Id" AS "CategoryId", c."Name" AS "CategoryName",
                       CAST(SUM(i."Quantity") AS integer) AS "QuantitySold", SUM(i."LineTotal") AS "Revenue"
                FROM "Sales" AS s
                INNER JOIN "SaleItems" AS i ON i."SaleId" = s."Id"
                INNER JOIN "Products" AS p ON p."Id" = i."ProductId"
                INNER JOIN "Categories" AS c ON c."Id" = p."CategoryId"
                WHERE s."IsDeleted" = FALSE AND s."SaleDate" >= {reportStart} AND s."SaleDate" < {reportEnd}
                GROUP BY c."Id", c."Name"
                ORDER BY "Revenue" DESC, c."Id"
                """)
            .ToListAsync(ct);
        var recentSales = await sales
            .Include(s => s.Items)
            .Include(s => s.Customer)
            .Include(s => s.Coupon)
            .OrderByDescending(s => s.SaleDate)
            .Take(8)
            .ToListAsync(ct);

        var lowStockQuery = db.Products.AsNoTracking()
            .Include(p => p.Category).Include(p => p.Images.Where(i => i.IsPrimary))
            .Where(p => p.StockQuantity <= p.MinStockThreshold);
        var lowStockCount = await lowStockQuery.CountAsync(ct);
        var lowStockProducts = await lowStockQuery
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .Take(20)
            .ToListAsync(ct);

        return new DashboardSummaryDto(
            totalSales,
            grossSales,
            grossSales,
            refunds,
            netRevenue,
            lowStockCount,
            dailySales,
            monthlyRevenue,
            topSellingProducts,
            salesByCategory,
            recentSales.Select(ToSaleDto).ToList(),
            lowStockProducts.Select(ToProductDto).ToList());
    }

    private static SaleDto ToSaleDto(Sale sale) => new(
        sale.Id, sale.InvoiceNumber, sale.SaleDate, sale.Subtotal, sale.DiscountAmount,
        sale.TaxAmount, sale.TotalAmount, sale.PaymentMethod, sale.Status,
        sale.Items.Select(i => new SaleItemDto(i.Id, i.ProductId, i.Sku, i.ProductName, i.Quantity, i.UnitPrice,
            i.DiscountAmount, i.TaxAmount, i.LineTotal)).ToList(), sale.CustomerId, sale.Customer?.Name,
        sale.Coupon?.Code, sale.LoyaltyPointsEarned, sale.LoyaltyPointsRedeemed);

    private static ProductDto ToProductDto(Product product) => new(
        product.Id, product.Sku, product.Name, product.Description, product.CategoryId, product.Category.Name,
        product.DefaultSupplierId,
        product.CostPrice, product.SalePrice, product.TaxRate, product.StockQuantity, product.MinStockThreshold,
        product.IsActive, product.StockQuantity <= product.MinStockThreshold,
        product.Images.FirstOrDefault(image => image.IsPrimary) is { } image
            ? $"/api/products/{product.Id}/images/{image.Id}" : null);
}
