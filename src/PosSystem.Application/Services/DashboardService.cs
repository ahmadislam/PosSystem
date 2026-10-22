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
            recentSales.Select(ToSaleDto).ToList(),
            lowStockProducts.Select(ToProductDto).ToList());
    }

    private static SaleDto ToSaleDto(Sale sale) => new(
        sale.Id, sale.InvoiceNumber, sale.SaleDate, sale.Subtotal, sale.DiscountAmount,
        sale.TaxAmount, sale.TotalAmount, sale.PaymentMethod, sale.Status,
        sale.Items.Select(i => new SaleItemDto(i.ProductId, i.Sku, i.ProductName, i.Quantity, i.UnitPrice,
            i.DiscountAmount, i.TaxAmount, i.LineTotal)).ToList());

    private static ProductDto ToProductDto(Product product) => new(
        product.Id, product.Sku, product.Name, product.Description, product.CategoryId, product.Category.Name,
        product.CostPrice, product.SalePrice, product.TaxRate, product.StockQuantity, product.MinStockThreshold,
        product.IsActive, product.StockQuantity <= product.MinStockThreshold);
}
