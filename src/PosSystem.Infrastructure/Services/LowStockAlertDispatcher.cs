using System.Text.Encodings.Web;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PosSystem.Application.Common;

namespace PosSystem.Infrastructure.Services;

public sealed class LowStockAlertDispatcher(
    IAppDbContext db,
    IEmailSender email,
    IOptions<EmailOptions> options,
    ILogger<LowStockAlertDispatcher> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task DispatchAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;

        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var alertIds = await db.Database.SqlQuery<int>(
                $"""
                 SELECT "Id" AS "Value"
                 FROM "LowStockAlerts"
                 WHERE "SentAt" IS NULL AND "ResolvedAt" IS NULL AND "NextAttemptAt" <= {now}
                 ORDER BY "DetectedAt", "Id"
                 FOR UPDATE SKIP LOCKED
                 LIMIT 20
                 """)
            .ToListAsync(ct);
        if (alertIds.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var alerts = await db.LowStockAlerts.IgnoreQueryFilters()
            .Include(alert => alert.Product)
            .Where(alert => alertIds.Contains(alert.Id) && alert.ResolvedAt == null && alert.SentAt == null)
            .ToListAsync(ct);
        foreach (var alert in alerts)
        {
            alert.DeliveryAttempts++;
            alert.LastAttemptAt = now;
            alert.NextAttemptAt = now.AddMinutes(10);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        try
        {
            var settings = await db.Settings.AsNoTracking()
                .Where(setting => setting.SettingKey == "ManagerEmail" || setting.SettingKey == "AlertsEnabled")
                .ToDictionaryAsync(setting => setting.SettingKey, setting => setting.Value, ct);
            if (!settings.TryGetValue("AlertsEnabled", out var enabledValue) ||
                !bool.TryParse(enabledValue, out var alertsEnabled))
                throw new InvalidOperationException("The AlertsEnabled setting is missing or invalid.");
            if (!alertsEnabled)
            {
                await SetNextAttemptAsync(alerts, DateTime.UtcNow.AddHours(1), null, ct);
                return;
            }
            if (!settings.TryGetValue("ManagerEmail", out var recipient) || !MailboxAddress.TryParse(recipient, out _))
                throw new InvalidOperationException("Configure a valid ManagerEmail before sending low-stock alerts.");

            var activeAlerts = alerts.Where(alert =>
                alert.ResolvedAt is null && alert.Product.StockQuantity <= alert.Product.MinStockThreshold).ToList();
            if (activeAlerts.Count == 0)
            {
                await SetNextAttemptAsync(alerts, DateTime.UtcNow, null, ct);
                return;
            }

            var rows = string.Join("", activeAlerts.Select(alert =>
                $"<tr><td>{HtmlEncoder.Default.Encode(alert.Product.Sku)}</td>" +
                $"<td>{HtmlEncoder.Default.Encode(alert.Product.Name)}</td>" +
                $"<td>{alert.Product.StockQuantity}</td><td>{alert.Product.MinStockThreshold}</td></tr>"));
            var body = $"""
                <h2>Low-stock alert</h2>
                <p>The following products are at or below their minimum stock threshold.</p>
                <table><thead><tr><th>SKU</th><th>Product</th><th>In stock</th><th>Threshold</th></tr></thead>
                <tbody>{rows}</tbody></table>
                """;
            await email.SendAsync(recipient, "POS low-stock alert", body, ct);

            var sentAt = DateTime.UtcNow;
            foreach (var alert in activeAlerts)
            {
                alert.SentAt = sentAt;
                alert.LastError = null;
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Sent low-stock alert for {ProductCount} products.", activeAlerts.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var retryAt = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(alerts.Max(a => a.DeliveryAttempts), 6))));
            await SetNextAttemptAsync(alerts, retryAt, ex.GetType().Name, ct);
            logger.LogError(ex, "Low-stock notification delivery failed; retry scheduled at {RetryAt}.", retryAt);
        }
    }

    private async Task SetNextAttemptAsync(
        IEnumerable<PosSystem.Domain.Entities.LowStockAlert> alerts,
        DateTime retryAt,
        string? errorType,
        CancellationToken ct)
    {
        foreach (var alert in alerts)
        {
            alert.NextAttemptAt = retryAt;
            alert.LastError = errorType;
        }
        await db.SaveChangesAsync(ct);
    }
}
