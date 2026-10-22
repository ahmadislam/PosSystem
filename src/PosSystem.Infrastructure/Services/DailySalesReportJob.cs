using System.Text.Encodings.Web;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PosSystem.Application.Common;

namespace PosSystem.Infrastructure.Services;

public sealed class DailySalesReportJob(
    IAppDbContext db,
    IEmailSender email,
    IOptions<EmailOptions> options,
    ILogger<DailySalesReportJob> logger)
{
    [AutomaticRetry(Attempts = 5)]
    public async Task SendAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            logger.LogWarning("Daily sales report skipped because email delivery is disabled.");
            return;
        }

        var recipient = await db.Settings.AsNoTracking()
            .Where(s => s.SettingKey == "ManagerEmail")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (!MailboxAddress.TryParse(recipient, out _))
            throw new InvalidOperationException("Configure a valid ManagerEmail in application settings before sending reports.");

        var end = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var start = end.AddDays(-1);
        var sales = db.Sales.AsNoTracking().Where(s => s.SaleDate >= start && s.SaleDate < end);

        var saleCount = await sales.CountAsync(ct);
        var revenue = await sales.SumAsync(s => (decimal?)s.TotalAmount, ct) ?? 0m;
        var paymentTotals = await sales
            .GroupBy(s => s.PaymentMethod)
            .Select(g => new { Method = g.Key.ToString(), Revenue = g.Sum(s => s.TotalAmount) })
            .OrderBy(x => x.Method)
            .ToListAsync(ct);
        var lowStock = await db.Products.AsNoTracking()
            .Where(p => p.StockQuantity <= p.MinStockThreshold)
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .Select(p => new { p.Sku, p.Name, p.StockQuantity, p.MinStockThreshold })
            .Take(20)
            .ToListAsync(ct);
        var lowStockCount = await db.Products.AsNoTracking()
            .CountAsync(p => p.StockQuantity <= p.MinStockThreshold, ct);
        var businessName = await db.Settings.AsNoTracking()
            .Where(s => s.SettingKey == "BusinessName")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct) ?? "POS System";

        var paymentRows = string.Join("", paymentTotals.Select(p =>
            $"<tr><td>{HtmlEncoder.Default.Encode(p.Method)}</td><td>{p.Revenue:N2}</td></tr>"));
        var inventoryRows = string.Join("", lowStock.Select(p =>
            $"<tr><td>{HtmlEncoder.Default.Encode(p.Sku)}</td><td>{HtmlEncoder.Default.Encode(p.Name)}</td>" +
            $"<td>{p.StockQuantity}</td><td>{p.MinStockThreshold}</td></tr>"));
        var inventoryTable = lowStock.Count == 0
            ? "<p>No products are below their stock threshold.</p>"
            : $"""
              <table><thead><tr><th>SKU</th><th>Product</th><th>In stock</th><th>Threshold</th></tr></thead>
              <tbody>{inventoryRows}</tbody></table>
              """;
        var body = $"""
            <h1>{HtmlEncoder.Default.Encode(businessName)} daily report</h1>
            <p>Sales for {start:yyyy-MM-dd} (UTC)</p>
            <p>Completed sales: {saleCount}<br>Total revenue: {revenue:N2}</p>
            <h2>Revenue by payment method</h2>
            <table><thead><tr><th>Payment method</th><th>Revenue</th></tr></thead><tbody>{paymentRows}</tbody></table>
            <h2>Inventory</h2>
            <p>Products at or below minimum stock: {lowStockCount}</p>
            {inventoryTable}
            """;

        await email.SendAsync(recipient, $"{businessName} daily sales report - {start:yyyy-MM-dd}", body, ct);
        logger.LogInformation("Sent daily sales report for {ReportDate} with {SaleCount} sales.", start, saleCount);
    }
}
