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
    public Task SendAsync(CancellationToken ct)
    {
        var end = DateTime.UtcNow.Date;
        return SendForPeriodAsync(end.AddDays(-1), end, "daily", ct);
    }

    [AutomaticRetry(Attempts = 5)]
    public Task SendMonthlyAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var end = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return SendForPeriodAsync(end.AddMonths(-1), end, "monthly", ct);
    }

    private async Task SendForPeriodAsync(DateTime start, DateTime end, string reportPeriod, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            logger.LogWarning("{ReportPeriod} sales report skipped because email delivery is disabled.", reportPeriod);
            return;
        }

        var configuredRecipient = await db.Settings.AsNoTracking()
            .Where(s => s.SettingKey == "ManagerEmail")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (configuredRecipient is null || !MailboxAddress.TryParse(configuredRecipient, out _))
            throw new InvalidOperationException("Configure a valid ManagerEmail in application settings before sending reports.");
        var recipient = configuredRecipient;

        var sales = db.Sales.AsNoTracking().Where(s => s.SaleDate >= start && s.SaleDate < end);

        var saleCount = await sales.CountAsync(ct);
        var grossRevenue = await sales.SumAsync(s => (decimal?)s.TotalAmount, ct) ?? 0m;
        var refunds = db.SaleReturns.AsNoTracking().Where(r => r.RefundDate >= start && r.RefundDate < end);
        var refundTotal = await refunds.SumAsync(r => (decimal?)r.TotalAmount, ct) ?? 0m;
        var netRevenue = grossRevenue - refundTotal;
        var paymentTotals = await sales
            .GroupBy(s => s.PaymentMethod)
            .Select(g => new { Method = g.Key, Revenue = g.Sum(s => s.TotalAmount) })
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
            $"<tr><td>{HtmlEncoder.Default.Encode(p.Method.ToString())}</td><td>{p.Revenue:N2}</td></tr>"));
        var inventoryRows = string.Join("", lowStock.Select(p =>
            $"<tr><td>{HtmlEncoder.Default.Encode(p.Sku)}</td><td>{HtmlEncoder.Default.Encode(p.Name)}</td>" +
            $"<td>{p.StockQuantity}</td><td>{p.MinStockThreshold}</td></tr>"));
        var inventoryTable = lowStock.Count == 0
            ? "<p>No products are below their stock threshold.</p>"
            : $"""
              <table><thead><tr><th>SKU</th><th>Product</th><th>In stock</th><th>Threshold</th></tr></thead>
              <tbody>{inventoryRows}</tbody></table>
              """;
        var periodLabel = reportPeriod == "daily"
            ? start.ToString("yyyy-MM-dd")
            : $"{start:yyyy-MM-dd} to {end.AddDays(-1):yyyy-MM-dd}";
        var body = $"""
            <h1>{HtmlEncoder.Default.Encode(businessName)} {reportPeriod} report</h1>
            <p>Period: {periodLabel} (UTC)</p>
            <p>Sales: {saleCount}<br>Gross sales: {grossRevenue:N2}<br>Refunds: {refundTotal:N2}<br>Net revenue: {netRevenue:N2}</p>
            <h2>Revenue by payment method</h2>
            <table><thead><tr><th>Payment method</th><th>Revenue</th></tr></thead><tbody>{paymentRows}</tbody></table>
            <h2>Inventory</h2>
            <p>Products at or below minimum stock: {lowStockCount}</p>
            {inventoryTable}
            """;

        await email.SendAsync(recipient, $"{businessName} {reportPeriod} sales report - {periodLabel}", body, ct);
        logger.LogInformation("Sent {ReportPeriod} sales report for {ReportPeriodLabel} with {SaleCount} sales.", reportPeriod, periodLabel, saleCount);
    }
}
