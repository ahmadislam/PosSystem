using System.Text.Encodings.Web;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PosSystem.Application.Common;

namespace PosSystem.Infrastructure.Services;

public sealed class LowStockEmailJob(
    IAppDbContext db,
    IEmailSender email,
    IOptions<EmailOptions> options,
    ILogger<LowStockEmailJob> logger)
{
    [AutomaticRetry(Attempts = 5)]
    public async Task SendAsync(int[] productIds, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            logger.LogWarning("Low-stock email job skipped because email delivery is disabled.");
            return;
        }

        var settings = await db.Settings.AsNoTracking()
            .Where(s => s.SettingKey == "ManagerEmail" || s.SettingKey == "AlertsEnabled")
            .ToDictionaryAsync(s => s.SettingKey, s => s.Value, ct);

        if (!settings.TryGetValue("AlertsEnabled", out var enabledValue) || !bool.TryParse(enabledValue, out var enabled))
            throw new InvalidOperationException("The AlertsEnabled setting is missing or invalid.");
        if (!enabled)
        {
            logger.LogInformation("Low-stock alerts are disabled in application settings.");
            return;
        }

        if (!settings.TryGetValue("ManagerEmail", out var recipient) || !MailboxAddress.TryParse(recipient, out _))
            throw new InvalidOperationException("Configure a valid ManagerEmail in application settings before sending alerts.");

        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.StockQuantity <= p.MinStockThreshold)
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .Select(p => new { p.Sku, p.Name, p.StockQuantity, p.MinStockThreshold })
            .ToListAsync(ct);
        if (products.Count == 0) return;

        var rows = string.Join("", products.Select(p =>
            $"<tr><td>{HtmlEncoder.Default.Encode(p.Sku)}</td><td>{HtmlEncoder.Default.Encode(p.Name)}</td>" +
            $"<td>{p.StockQuantity}</td><td>{p.MinStockThreshold}</td></tr>"));
        var body = $"""
            <h2>Low-stock alert</h2>
            <p>The following products are at or below their minimum stock threshold.</p>
            <table><thead><tr><th>SKU</th><th>Product</th><th>In stock</th><th>Threshold</th></tr></thead>
            <tbody>{rows}</tbody></table>
            """;

        await email.SendAsync(recipient, "POS low-stock alert", body, ct);
        logger.LogInformation("Sent low-stock alert for {ProductCount} products.", products.Count);
    }
}
