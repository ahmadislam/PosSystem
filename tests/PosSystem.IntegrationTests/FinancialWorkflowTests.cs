using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;
using PosSystem.Infrastructure.Services;

namespace PosSystem.IntegrationTests;

public sealed class FinancialWorkflowTests
{
    [PgFact]
    public async Task Retrying_checkout_returns_original_tender_and_deducts_stock_once()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync();
        await using (var db = database.Open()) await database.Shifts(db).OpenAsync(100m);
        var request = PostgresDatabase.CashSale(product.Id, tender: 20m);
        var key = Guid.NewGuid().ToString();
        int saleId;
        await using (var db = database.Open())
        {
            var result = await database.Sales(db).CreateAsync(request, key);
            saleId = result.Id;
            Assert.Equal(20m, result.CashTendered);
            Assert.Equal(10m, result.ChangeDue);
        }
        await using (var db = database.Open())
        {
            var result = await database.Sales(db).CreateAsync(request, key);
            Assert.Equal(saleId, result.Id);
            Assert.Equal(10m, result.ChangeDue);
        }
        await using var verify = database.Open();
        Assert.Single(await verify.Sales.ToListAsync());
        Assert.Equal(19, await verify.Products.Select(p => p.StockQuantity).SingleAsync());
        Assert.Single(await verify.StockMovements.Where(m => m.Type == StockMovementType.Sale).ToListAsync());
        Assert.Contains(await verify.AuditLogs.ToListAsync(), log => log.EntityName == "Sale");
    }

    [PgFact]
    public async Task Cash_tender_below_current_server_price_rejects_checkout_without_changes()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync(price: 12m);
        await using (var db = database.Open()) await database.Shifts(db).OpenAsync(100m);
        await using (var db = database.Open())
            await Assert.ThrowsAsync<BadRequestException>(() => database.Sales(db)
                .CreateAsync(PostgresDatabase.CashSale(product.Id, tender: 10m), Guid.NewGuid().ToString()));
        await using var verify = database.Open();
        Assert.Empty(await verify.Sales.ToListAsync());
        Assert.Equal(20, await verify.Products.Select(p => p.StockQuantity).SingleAsync());
    }

    [PgTheory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Repeated_partial_returns_reverse_earned_and_restore_redeemed_points_once(int redeemed)
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync(price: 50m);
        int customerId;
        await using (var db = database.Open())
        {
            var customer = new Customer { Name = "Loyalty customer", LoyaltyPoints = 200, IsActive = true };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            await database.Shifts(db).OpenAsync(100m);
        }
        SaleDto sale;
        await using (var db = database.Open())
        {
            var request = PostgresDatabase.CashSale(product.Id, 2);
            request.CustomerId = customerId;
            request.LoyaltyPointsToRedeem = redeemed;
            sale = await database.Sales(db).CreateAsync(request, Guid.NewGuid().ToString());
        }
        for (var i = 0; i < 2; i++)
        {
            await using var db = database.Open();
            await database.Returns(db).CreateAsync(sale.Id, new CreateSaleReturnRequest
            {
                RefundMethod = PaymentMethod.Cash, Reason = "Integration return",
                Items = [new SaleReturnLineRequest { SaleItemId = sale.Items[0].SaleItemId, Quantity = 1 }]
            });
        }
        await using var verify = database.Open();
        Assert.Equal(200, await verify.Customers.Where(c => c.Id == customerId).Select(c => c.LoyaltyPoints).SingleAsync());
        Assert.Equal(-sale.LoyaltyPointsEarned, await verify.LoyaltyTransactions
            .Where(e => e.Type == "ReturnEarnedReversal").SumAsync(e => e.PointsChange));
        Assert.Equal(redeemed, await verify.LoyaltyTransactions
            .Where(e => e.Type == "ReturnRedemptionRestore").SumAsync(e => e.PointsChange));
        Assert.Equal(20, await verify.Products.Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(SaleStatus.Refunded, await verify.Sales.Select(s => s.Status).SingleAsync());
        var pending = await database.Shifts(verify).BeginCloseAsync();
        Assert.Equal(100m, pending.ExpectedCash);
    }

    [PgFact]
    public async Task Fully_loyalty_paid_sale_restores_only_each_returned_share_of_points()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync(price: 0.50m);
        int customerId;
        await using (var db = database.Open())
        {
            var customer = new Customer { Name = "Points-only customer", LoyaltyPoints = 100, IsActive = true };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            await database.Shifts(db).OpenAsync(0m);
        }
        SaleDto sale;
        await using (var db = database.Open())
        {
            var request = PostgresDatabase.CashSale(product.Id, 2, tender: 0m);
            request.CustomerId = customerId;
            request.LoyaltyPointsToRedeem = 100;
            sale = await database.Sales(db).CreateAsync(request, Guid.NewGuid().ToString());
            Assert.Equal(0m, sale.TotalAmount);
        }
        for (var i = 1; i <= 2; i++)
        {
            await using var db = database.Open();
            var returned = await database.Returns(db).CreateAsync(sale.Id, new CreateSaleReturnRequest
            {
                RefundMethod = PaymentMethod.Cash,
                Items = [new SaleReturnLineRequest { SaleItemId = sale.Items[0].SaleItemId, Quantity = 1 }]
            });
            Assert.Equal(0m, returned.TotalAmount);
            await using var verify = database.Open();
            Assert.Equal(i * 50, await verify.Customers.Where(c => c.Id == customerId)
                .Select(c => c.LoyaltyPoints).SingleAsync());
        }
    }

    [PgFact]
    public async Task Tiny_coupon_basket_allocates_only_nonnegative_cents_and_preserves_total()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var first = await database.SeedAsync(price: 0.01m);
        int[] ids;
        await using (var db = database.Open())
        {
            for (var i = 0; i < 3; i++) db.Products.Add(new Product
            {
                CategoryId = first.CategoryId, Sku = "PENNY-" + i, Name = "Penny " + i,
                SalePrice = 0.01m, StockQuantity = 10, IsActive = true
            });
            db.Coupons.Add(new Coupon
            {
                Code = "HALF", Name = "Half off", IsPercentage = true, Value = 50m,
                StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(1), IsActive = true
            });
            await db.SaveChangesAsync();
            ids = await db.Products.OrderBy(p => p.Id).Select(p => p.Id).ToArrayAsync();
            await database.Shifts(db).OpenAsync(0m);
        }
        await using var selling = database.Open();
        var sale = await database.Sales(selling).CreateAsync(new CreateSaleRequest
        {
            PaymentMethod = PaymentMethod.Cash, CashTendered = 1m, CouponCode = "HALF",
            Items = ids.Select(id => new SaleLineRequest { ProductId = id, Quantity = 1 }).ToList()
        }, Guid.NewGuid().ToString());
        Assert.Equal(0.02m, sale.DiscountAmount);
        Assert.Equal(0.02m, sale.TotalAmount);
        Assert.Equal(0.98m, sale.ChangeDue);
        Assert.All(sale.Items, item => Assert.InRange(item.DiscountAmount, 0m, 0.01m));
        Assert.Equal(sale.DiscountAmount, sale.Items.Sum(item => item.DiscountAmount));
    }

    [PgFact]
    public async Task Shift_close_holds_row_lock_until_cash_snapshot_is_committed()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync();
        await using (var db = database.Open()) await database.Shifts(db).OpenAsync(100m);
        var pause = new PauseAtCashTotal();
        await using var closing = database.Open(pause);
        var closeTask = database.Shifts(closing).BeginCloseAsync();
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var started = new ObserveShiftLock();
        await using var selling = database.Open(started);
        var saleTask = database.Sales(selling).CreateAsync(PostgresDatabase.CashSale(product.Id), Guid.NewGuid().ToString());
        try
        {
            await started.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(saleTask.IsCompleted, "A checkout must wait while the closing transaction owns the shift lock.");
        }
        finally { pause.Release.TrySetResult(); }
        var result = await closeTask.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<BadRequestException>(() => saleTask.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(100m, result.ExpectedCash);
        Assert.Equal(ShiftStatus.PendingClose, result.Status);
        await using var verify = database.Open();
        Assert.Empty(await verify.Sales.ToListAsync());
        Assert.Equal(20, await verify.Products.Select(p => p.StockQuantity).SingleAsync());
    }

    [PgFact]
    public async Task Concurrent_purchase_receipts_create_one_stock_movement()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync();
        int orderId;
        await using (var db = database.Open())
        {
            var supplier = new Supplier { Name = "Integration supplier", IsActive = true };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            var purchasing = new PurchaseOrderService(db, database.User, new HangfireLowStockNotifier(db));
            var order = await purchasing.CreateAsync(new PurchaseOrderRequest
            {
                SupplierId = supplier.Id,
                Items = [new PurchaseOrderLineRequest { ProductId = product.Id, Quantity = 3, UnitCost = 2m }]
            });
            await purchasing.OrderAsync(order.Id);
            orderId = order.Id;
        }
        async Task<bool> ReceiveAsync()
        {
            await using var db = database.Open();
            try
            {
                await new PurchaseOrderService(db, database.User, new HangfireLowStockNotifier(db)).ReceiveAsync(orderId);
                return true;
            }
            catch (ConflictException) { return false; }
        }
        var results = await Task.WhenAll(ReceiveAsync(), ReceiveAsync());
        Assert.Single(results, success => success);
        await using var verify = database.Open();
        Assert.Equal(23, await verify.Products.Select(p => p.StockQuantity).SingleAsync());
        Assert.Single(await verify.StockMovements.Where(m => m.Type == StockMovementType.Purchase).ToListAsync());
    }

    [PgFact]
    public async Task Failed_audit_write_rolls_back_ordinary_stock_adjustment()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync();
        await using (var db = database.Open(new RejectAuditInsert()))
        {
            var failure = await Record.ExceptionAsync(() => new ProductService(db, database.User, new HangfireLowStockNotifier(db))
                .AdjustStockAsync(product.Id, new StockAdjustRequest { QuantityChange = -1, Note = "Must roll back" }));
            Assert.NotNull(failure);
            Assert.Contains("Simulated unavailable audit store", failure.ToString());
        }
        await using var verify = database.Open();
        Assert.Equal(20, await verify.Products.Select(p => p.StockQuantity).SingleAsync());
        Assert.Empty(await verify.StockMovements.ToListAsync());
    }

    private sealed class PauseAtCashTotal : DbCommandInterceptor
    {
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("sum(", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("\"Sales\""))
            {
                Assert.NotNull(eventData.Context!.Database.CurrentTransaction);
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ObserveShiftLock : DbCommandInterceptor
    {
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"CashierShifts\"") && command.CommandText.Contains("FOR UPDATE")) Reached.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RejectAuditInsert : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"AuditLogs\"")) throw new IOException("Simulated unavailable audit store");
            return ValueTask.FromResult(result);
        }
    }
}
