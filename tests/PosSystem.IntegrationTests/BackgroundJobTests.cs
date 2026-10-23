using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Infrastructure.Services;

namespace PosSystem.IntegrationTests;

public sealed class BackgroundJobTests
{
    [PgFact]
    public async Task Low_stock_outbox_retries_delivery_and_resolves_after_replenishment()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync(stock: 3);
        await using (var db = database.Open())
        {
            await database.Shifts(db).OpenAsync(0m);
            await database.Sales(db).CreateAsync(PostgresDatabase.CashSale(product.Id), Guid.NewGuid().ToString());
        }
        var sender = new CapturingSender { FailuresRemaining = 1 };
        var options = Options.Create(new EmailOptions { Enabled = true });
        await using (var db = database.Open())
            await new LowStockAlertDispatcher(db, sender, options, NullLogger<LowStockAlertDispatcher>.Instance)
                .DispatchAsync(CancellationToken.None);

        await using (var db = database.Open())
        {
            var alert = await db.LowStockAlerts.SingleAsync();
            Assert.Null(alert.SentAt);
            Assert.Equal(1, alert.DeliveryAttempts);
            Assert.Equal(nameof(IOException), alert.LastError);
            Assert.True(alert.NextAttemptAt > DateTime.UtcNow);
            alert.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
            await new LowStockAlertDispatcher(db, sender, options, NullLogger<LowStockAlertDispatcher>.Instance)
                .DispatchAsync(CancellationToken.None);
        Assert.Single(sender.Messages);
        Assert.Contains("TEST-SKU", sender.Messages[0].Body);
        await using (var db = database.Open())
        {
            var alert = await db.LowStockAlerts.SingleAsync();
            Assert.NotNull(alert.SentAt);
            Assert.Null(alert.LastError);
            Assert.Equal(2, alert.DeliveryAttempts);
            await new LowStockAlertDispatcher(db, sender, options, NullLogger<LowStockAlertDispatcher>.Instance)
                .DispatchAsync(CancellationToken.None);
        }
        Assert.Single(sender.Messages);
        await using (var db = database.Open())
            await new ProductService(db, database.User, new HangfireLowStockNotifier(db)).AdjustStockAsync(product.Id,
                new StockAdjustRequest { QuantityChange = 10, Note = "Replenished" });
        await using var verify = database.Open();
        Assert.NotNull((await verify.LowStockAlerts.SingleAsync()).ResolvedAt);
        Assert.Single(await verify.Sales.ToListAsync());
    }

    [PgFact]
    public async Task Daily_summary_uses_previous_utc_day_and_reports_gross_refund_and_net()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync(price: 10m);
        int saleId;
        await using (var db = database.Open())
        {
            await database.Shifts(db).OpenAsync(0m);
            var sale = await database.Sales(db).CreateAsync(PostgresDatabase.CashSale(product.Id, 2), Guid.NewGuid().ToString());
            saleId = sale.Id;
            await database.Returns(db).CreateAsync(sale.Id, new CreateSaleReturnRequest
            {
                RefundMethod = PosSystem.Domain.Enums.PaymentMethod.Cash,
                Items = [new SaleReturnLineRequest { SaleItemId = sale.Items[0].SaleItemId, Quantity = 1 }]
            });
        }
        await using (var db = database.Open())
        {
            (await db.Sales.SingleAsync(s => s.Id == saleId)).SaleDate = DateTime.UtcNow.Date.AddHours(-12);
            (await db.SaleReturns.SingleAsync()).RefundDate = DateTime.UtcNow.Date.AddHours(-11);
            db.Settings.Add(new Setting { SettingKey = "BusinessName", Value = "<Test Shop>" });
            await db.SaveChangesAsync();
        }
        var sender = new CapturingSender();
        await using (var db = database.Open())
            await new DailySalesReportJob(db, sender, Options.Create(new EmailOptions { Enabled = true }),
                NullLogger<DailySalesReportJob>.Instance).SendAsync(CancellationToken.None);
        var message = Assert.Single(sender.Messages);
        Assert.Equal("manager@example.test", message.Recipient);
        Assert.Contains(DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"), message.Subject);
        Assert.Contains("Gross sales: 20", message.Body);
        Assert.Contains("Refunds: 10", message.Body);
        Assert.Contains("Net revenue: 10", message.Body);
        Assert.Contains("&lt;Test Shop&gt;", message.Body);
    }

    private sealed class CapturingSender : IEmailSender
    {
        internal int FailuresRemaining { get; set; }
        internal List<(string Recipient, string Subject, string Body)> Messages { get; } = [];
        public Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (FailuresRemaining-- > 0) throw new IOException("Simulated SMTP outage");
            Messages.Add((recipient, subject, htmlBody));
            return Task.CompletedTask;
        }
    }
}
