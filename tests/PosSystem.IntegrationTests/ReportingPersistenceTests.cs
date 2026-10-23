using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;
using PosSystem.Infrastructure.Services;

namespace PosSystem.IntegrationTests;

public sealed class ReportingPersistenceTests
{
    [PgFact]
    public async Task Creation_audit_uses_persisted_foreign_keys_in_new_entity_graphs()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        int categoryId;
        int productId;
        await using (var db = database.Open())
        {
            var category = new Category { Name = "New category" };
            var product = new Product { Category = category, Sku = "GRAPH-001", Name = "New product" };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            categoryId = category.Id;
            productId = product.Id;
        }
        await using var verify = database.Open();
        var created = await verify.AuditLogs.SingleAsync(log => log.EntityName == nameof(Product)
            && log.EntityId == productId.ToString() && log.Action == AuditAction.Create);
        using var snapshot = JsonDocument.Parse(created.NewValues!);
        Assert.True(categoryId > 0);
        Assert.Equal(categoryId, snapshot.RootElement.GetProperty("CategoryId").GetInt32());
        Assert.False(snapshot.RootElement.TryGetProperty("PasswordHash", out _));
    }

    [PgFact]
    public async Task Dashboard_sql_keeps_historical_products_and_utc_boundaries_without_deleted_sales()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync(price: 10m);
        var day = DateTime.UtcNow.Date;
        await using (var db = database.Open())
        {
            await database.Shifts(db).OpenAsync(0m);
            var current = await database.Sales(db).CreateAsync(PostgresDatabase.CashSale(product.Id, 2), Guid.NewGuid().ToString());
            var previous = await database.Sales(db).CreateAsync(PostgresDatabase.CashSale(product.Id, 3), Guid.NewGuid().ToString());
            var next = await database.Sales(db).CreateAsync(PostgresDatabase.CashSale(product.Id, 4), Guid.NewGuid().ToString());
            var deleted = await database.Sales(db).CreateAsync(PostgresDatabase.CashSale(product.Id), Guid.NewGuid().ToString());
            await database.Returns(db).CreateAsync(current.Id, new CreateSaleReturnRequest
            {
                RefundMethod = PaymentMethod.Cash,
                Items = [new SaleReturnLineRequest { SaleItemId = current.Items[0].SaleItemId, Quantity = 1 }]
            });
            (await db.Sales.SingleAsync(s => s.Id == current.Id)).SaleDate = day;
            (await db.Sales.SingleAsync(s => s.Id == previous.Id)).SaleDate = day.AddTicks(-1);
            (await db.Sales.SingleAsync(s => s.Id == next.Id)).SaleDate = day.AddDays(1);
            (await db.Sales.SingleAsync(s => s.Id == deleted.Id)).IsDeleted = true;
            (await db.Products.SingleAsync()).IsDeleted = true;
            (await db.Categories.SingleAsync()).IsDeleted = true;
            await db.SaveChangesAsync();
        }

        await using var verify = database.Open();
        var dashboard = new DashboardService(verify);
        var summary = await dashboard.GetSummaryAsync(day, day);
        Assert.Equal(1, summary.TotalSales);
        Assert.Equal(20m, summary.GrossSales);
        Assert.Equal(10m, summary.Refunds);
        Assert.Equal(10m, summary.NetRevenue);
        var top = Assert.Single(summary.TopSellingProducts);
        Assert.Equal("TEST-SKU", top.Sku);
        Assert.Equal("Test product", top.ProductName);
        Assert.Equal(2, top.QuantitySold);
        Assert.Equal(20m, top.Revenue);
        var category = Assert.Single(summary.SalesByCategory);
        Assert.Equal("Integration", category.CategoryName);
        Assert.Equal(2, category.QuantitySold);
        Assert.Equal(20m, category.Revenue);
        Assert.Empty(await verify.Products.ToListAsync());
        Assert.Single(await verify.Products.IgnoreQueryFilters().ToListAsync());

        // Omitting a date range must preserve the existing all-time dashboard contract.
        var allTime = await dashboard.GetSummaryAsync(null, null);
        Assert.Equal(3, allTime.TotalSales);
        Assert.Equal(9, Assert.Single(allTime.TopSellingProducts).QuantitySold);
        Assert.Equal(90m, Assert.Single(allTime.SalesByCategory).Revenue);
    }

    [PgFact]
    public async Task Export_workbooks_contain_numeric_sales_returns_and_visible_inventory()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync(price: 12.50m);
        await using (var db = database.Open())
        {
            await database.Shifts(db).OpenAsync(0m);
            var sale = await database.Sales(db).CreateAsync(PostgresDatabase.CashSale(product.Id, 2), Guid.NewGuid().ToString());
            await database.Returns(db).CreateAsync(sale.Id, new CreateSaleReturnRequest
            {
                RefundMethod = PaymentMethod.Cash,
                Items = [new SaleReturnLineRequest { SaleItemId = sale.Items[0].SaleItemId, Quantity = 1 }]
            });
            db.Products.Add(new Product
            {
                CategoryId = product.CategoryId, Sku = "HIDDEN", Name = "Deleted product",
                StockQuantity = 100, SalePrice = 99m, IsDeleted = true
            });
            await db.SaveChangesAsync();
        }
        await using var verify = database.Open();
        var reports = new ExcelReportService(verify);
        using var salesStream = new MemoryStream(await reports.CreateSalesReportAsync(DateTime.UtcNow.Date, DateTime.UtcNow.Date));
        using var sales = new XLWorkbook(salesStream);
        Assert.Equal(25m, sales.Worksheet("Summary").Cell(4, 2).GetValue<decimal>());
        Assert.Equal(12.50m, sales.Worksheet("Summary").Cell(6, 2).GetValue<decimal>());
        Assert.Equal(12.50m, sales.Worksheet("Summary").Cell(7, 2).GetValue<decimal>());
        Assert.Equal(25m, sales.Worksheet("Sales").Cell(2, 9).GetValue<decimal>());
        Assert.Equal(12.50m, sales.Worksheet("Returns").Cell(2, 8).GetValue<decimal>());
        Assert.Equal(XLDataType.Number, sales.Worksheet("Sales").Cell(2, 9).DataType);

        using var inventoryStream = new MemoryStream(await reports.CreateInventoryReportAsync());
        using var inventory = new XLWorkbook(inventoryStream);
        Assert.Equal(1, inventory.Worksheet("Summary").Cell(3, 2).GetValue<int>());
        Assert.Equal("TEST-SKU", inventory.Worksheet("Inventory").Cell(2, 1).GetString());
        Assert.Equal(19, inventory.Worksheet("Inventory").Cell(2, 6).GetValue<int>());
        Assert.Equal(2, inventory.Worksheet("Inventory").LastRowUsed()!.RowNumber());
    }

    [PgFact]
    public async Task Product_soft_delete_audits_old_and_new_values_and_critical_indexes_exist()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var product = await database.SeedAsync();
        await using (var db = database.Open())
        {
            var entity = await db.Products.SingleAsync();
            entity.Name = "Updated product";
            await db.SaveChangesAsync();
            db.Products.Remove(entity);
            await db.SaveChangesAsync();
        }
        await using var verify = database.Open();
        Assert.Empty(await verify.Products.ToListAsync());
        Assert.True((await verify.Products.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        var audits = await verify.AuditLogs.Where(log => log.EntityName == nameof(Product) && log.EntityId == product.Id.ToString())
            .ToListAsync();
        Assert.Contains(audits, log => log.Action == AuditAction.Create);
        var update = Assert.Single(audits, log => log.Action == AuditAction.Update);
        Assert.Equal(database.User.UserId, update.UserId);
        Assert.Equal("Test product", JsonDocument.Parse(update.OldValues!).RootElement.GetProperty("Name").GetString());
        Assert.Equal("Updated product", JsonDocument.Parse(update.NewValues!).RootElement.GetProperty("Name").GetString());
        var deletion = Assert.Single(audits, log => log.Action == AuditAction.Delete);
        Assert.False(JsonDocument.Parse(deletion.OldValues!).RootElement.GetProperty("IsDeleted").GetBoolean());
        Assert.True(JsonDocument.Parse(deletion.NewValues!).RootElement.GetProperty("IsDeleted").GetBoolean());
        Assert.True(deletion.Timestamp <= DateTime.UtcNow);

        var indexes = await verify.Database.SqlQuery<string>($"""
            SELECT indexname AS "Value" FROM pg_indexes WHERE schemaname = 'public'
            """).ToListAsync();
        Assert.Contains("IX_Products_Sku", indexes);
        Assert.Contains("IX_Sales_InvoiceNumber", indexes);
        Assert.Contains("IX_Sales_SaleDate", indexes);
    }
}
