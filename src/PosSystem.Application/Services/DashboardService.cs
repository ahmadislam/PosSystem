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
        var revenue = await sales.SumAsync(s => (decimal?)s.TotalAmount, ct) ?? 0m;
        var dailySales = await sales
            .GroupBy(s => s.SaleDate.Date)
            .Select(g => new DailySalesDto(g.Key, g.Sum(s => s.TotalAmount)))
            .OrderBy(d => d.Date)
            .ToListAsync(ct);
        var monthlyTotals = await sales
            .GroupBy(s => new { s.SaleDate.Year, s.SaleDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(s => s.TotalAmount) })
            .OrderBy(g => g.Year)
            .ThenBy(g => g.Month)
            .ToListAsync(ct);
        var monthlyRevenue = monthlyTotals
            .Select(g => new MonthlyRevenueDto(new DateTime(g.Year, g.Month, 1, 0, 0, 0, DateTimeKind.Utc), g.Revenue))
            .ToList();

        var saleItems = from sale in sales
                        join item in db.SaleItems.AsNoTracking() on sale.Id equals item.SaleId
                        select item;
        var topSellingProducts = await saleItems
            .GroupBy(item => new { item.Sku, item.ProductName })
            .Select(group => new TopSellingProductDto(
                group.Key.Sku, group.Key.ProductName, group.Sum(item => item.Quantity), group.Sum(item => item.LineTotal)))
            .OrderByDescending(product => product.QuantitySold)
            .ThenByDescending(product => product.Revenue)
            .Take(5)
            .ToListAsync(ct);

        var salesByCategory = await (
                from item in saleItems
                join product in db.Products.IgnoreQueryFilters().AsNoTracking() on item.ProductId equals product.Id
                join category in db.Categories.IgnoreQueryFilters().AsNoTracking() on product.CategoryId equals category.Id
                group item by new { category.Id, category.Name } into categoryGroup
                orderby categoryGroup.Sum(item => item.LineTotal) descending
                select new CategorySalesDto(
                    categoryGroup.Key.Id, categoryGroup.Key.Name,
                    categoryGroup.Sum(item => item.Quantity), categoryGroup.Sum(item => item.LineTotal)))
            .ToListAsync(ct);
        var recentSales = await sales
            .Include(s => s.Items)
            .OrderByDescending(s => s.SaleDate)
            .Take(8)
            .ToListAsync(ct);

        var lowStockQuery = db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.StockQuantity <= p.MinStockThreshold);
        var lowStockCount = await lowStockQuery.CountAsync(ct);
        var lowStockProducts = await lowStockQuery
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .Take(20)
            .ToListAsync(ct);

        return new DashboardSummaryDto(
            totalSales,
            revenue,
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
            i.DiscountAmount, i.TaxAmount, i.LineTotal)).ToList());

    private static ProductDto ToProductDto(Product product) => new(
        product.Id, product.Sku, product.Name, product.Description, product.CategoryId, product.Category.Name,
        product.CostPrice, product.SalePrice, product.TaxRate, product.StockQuantity, product.MinStockThreshold,
        product.IsActive, product.StockQuantity <= product.MinStockThreshold);
}
