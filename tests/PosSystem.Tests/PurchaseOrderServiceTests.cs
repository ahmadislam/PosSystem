using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;
using PosSystem.Infrastructure.Persistence;
using PosSystem.Infrastructure.Services;

namespace PosSystem.Tests;

public sealed class PurchaseOrderServiceTests
{
    [Fact]
    public async Task Receive_moves_ordered_stock_once_and_audits_the_changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var supplier = await fixture.Db.Suppliers.SingleAsync();
        var product = await fixture.Db.Products.SingleAsync();
        var service = fixture.PurchaseOrders;
        var order = await service.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.Id,
            Items = [new PurchaseOrderLineRequest { ProductId = product.Id, Quantity = 6, UnitCost = 3.25m }]
        });

        await service.OrderAsync(order.Id);
        var received = await service.ReceiveAsync(order.Id);

        Assert.Equal(PurchaseOrderStatus.Received, received.Status);
        Assert.Equal(10, await fixture.Db.Products.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
        var movement = await fixture.Db.StockMovements.SingleAsync();
        Assert.Equal(6, movement.QuantityChange);
        Assert.Equal(StockMovementType.Purchase, movement.Type);
        Assert.Equal("PurchaseOrder", movement.ReferenceType);
        Assert.Equal(order.Id, movement.ReferenceId);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), log =>
            log.EntityName == "PurchaseOrder" && log.Action == AuditAction.Update);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), log =>
            log.EntityName == "Product" && log.Action == AuditAction.Update);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), log =>
            log.EntityName == "StockMovement" && log.Action == AuditAction.Create);
    }

    [Fact]
    public async Task Receiving_again_is_rejected_without_a_second_stock_movement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.Db.Products.SingleAsync();
        var order = await CreateOrderedOrder(fixture, product.Id, 3);
        var service = fixture.PurchaseOrders;
        await service.ReceiveAsync(order.Id);

        await Assert.ThrowsAsync<ConflictException>(() => service.ReceiveAsync(order.Id));

        Assert.Equal(7, await fixture.Db.Products.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
        Assert.Single(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task Draft_purchase_order_can_replace_its_items()
    {
        await using var fixture = await Fixture.CreateAsync();
        var supplier = await fixture.Db.Suppliers.SingleAsync();
        var product = await fixture.Db.Products.SingleAsync();
        var service = fixture.PurchaseOrders;
        var order = await service.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.Id,
            Items = [new PurchaseOrderLineRequest { ProductId = product.Id, Quantity = 2, UnitCost = 2 }]
        });

        await service.UpdateDraftAsync(order.Id, new PurchaseOrderRequest
        {
            SupplierId = supplier.Id,
            Items = [new PurchaseOrderLineRequest { ProductId = product.Id, Quantity = 7, UnitCost = 3 }]
        });

        var savedItem = await fixture.Db.PurchaseOrderItems.SingleAsync();
        Assert.Equal(7, savedItem.Quantity);
        Assert.Equal(3m, savedItem.UnitCost);
    }

    [Fact]
    public async Task Failed_receipt_rolls_back_every_stock_update_and_movement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var normalProduct = await fixture.Db.Products.SingleAsync();
        var categoryId = await fixture.Db.Categories.Select(c => c.Id).SingleAsync();
        var overflowProduct = new Product
        {
            Sku = "OVERFLOW",
            Name = "Overflow item",
            CategoryId = categoryId,
            CostPrice = 1,
            SalePrice = 2,
            StockQuantity = int.MaxValue,
            IsActive = true
        };
        fixture.Db.Products.Add(overflowProduct);
        await fixture.Db.SaveChangesAsync();
        var supplierId = await fixture.Db.Suppliers.Select(s => s.Id).SingleAsync();
        var service = fixture.PurchaseOrders;
        var order = await service.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplierId,
            Items =
            [
                new PurchaseOrderLineRequest { ProductId = normalProduct.Id, Quantity = 2, UnitCost = 1 },
                new PurchaseOrderLineRequest { ProductId = overflowProduct.Id, Quantity = 1, UnitCost = 1 }
            ]
        });
        await service.OrderAsync(order.Id);

        await Assert.ThrowsAsync<ConflictException>(() => service.ReceiveAsync(order.Id));

        await using var verificationDb = new AppDbContext(fixture.Options, fixture.CurrentUser);
        Assert.Equal(4, await verificationDb.Products.Where(p => p.Id == normalProduct.Id)
            .Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(PurchaseOrderStatus.Ordered, await verificationDb.PurchaseOrders
            .Where(p => p.Id == order.Id).Select(p => p.Status).SingleAsync());
        Assert.Empty(await verificationDb.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task Supplier_delete_is_soft_and_hides_it_from_normal_queries()
    {
        await using var fixture = await Fixture.CreateAsync();
        var supplier = await fixture.Db.Suppliers.SingleAsync();
        await new SupplierService(fixture.Db).DeleteAsync(supplier.Id);

        Assert.Empty(await fixture.Db.Suppliers.ToListAsync());
        Assert.True(await fixture.Db.Suppliers.IgnoreQueryFilters().Where(s => s.Id == supplier.Id)
            .Select(s => s.IsDeleted).SingleAsync());
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), log =>
            log.EntityName == "Supplier" && log.Action == AuditAction.Delete);
    }

    [Fact]
    public async Task Low_stock_alert_is_created_and_resolved_after_replenishment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var product = await fixture.Db.Products.SingleAsync();
        product.MinStockThreshold = product.StockQuantity;
        var notifier = new HangfireLowStockNotifier(fixture.Db);

        await notifier.CheckAsync([product.Id]);
        await fixture.Db.SaveChangesAsync();
        var alert = await fixture.Db.LowStockAlerts.SingleAsync();
        Assert.Null(alert.ResolvedAt);
        Assert.Equal(product.Id, alert.ProductId);

        product.StockQuantity++;
        await notifier.CheckAsync([product.Id]);
        await fixture.Db.SaveChangesAsync();

        Assert.NotNull(await fixture.Db.LowStockAlerts.Select(a => a.ResolvedAt).SingleAsync());
    }

    private static async Task<PurchaseOrderDto> CreateOrderedOrder(Fixture fixture, int productId, int quantity)
    {
        var service = fixture.PurchaseOrders;
        var supplierId = await fixture.Db.Suppliers.Select(s => s.Id).SingleAsync();
        var order = await service.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplierId,
            Items = [new PurchaseOrderLineRequest { ProductId = productId, Quantity = quantity, UnitCost = 2 }]
        });
        return await service.OrderAsync(order.Id);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public TestCurrentUser CurrentUser { get; }
        public DbContextOptions<AppDbContext> Options { get; }
        public PurchaseOrderService PurchaseOrders { get; }

        private Fixture(SqliteConnection connection, AppDbContext db, TestCurrentUser currentUser,
            DbContextOptions<AppDbContext> options)
        {
            _connection = connection;
            Db = db;
            CurrentUser = currentUser;
            Options = options;
            PurchaseOrders = new PurchaseOrderService(db, currentUser, new HangfireLowStockNotifier(db));
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var currentUser = new TestCurrentUser();
            var db = new AppDbContext(options, currentUser);
            var fixture = new Fixture(connection, db, currentUser, options);
            await CreateSchemaAsync(db);
            db.Users.Add(new User { Username = "manager", Email = "manager@example.test", PasswordHash = "hash" });
            db.Categories.Add(new Category { Name = "Hardware" });
            db.Suppliers.Add(new Supplier { Name = "Acme Supply" });
            await db.SaveChangesAsync();
            var category = await db.Categories.SingleAsync();
            var supplier = await db.Suppliers.SingleAsync();
            db.Products.Add(new Product
            {
                Sku = "WIDGET-1",
                Name = "Widget",
                CategoryId = category.Id,
                DefaultSupplierId = supplier.Id,
                CostPrice = 2,
                SalePrice = 5,
                StockQuantity = 4,
                IsActive = true
            });
            await db.SaveChangesAsync();
            fixture.CurrentUser.UserIdValue = await db.Users.Select(u => u.Id).SingleAsync();
            return fixture;
        }

        private static async Task CreateSchemaAsync(AppDbContext db)
        {
            string[] statements =
            [
                """CREATE TABLE "Users" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "Username" TEXT NOT NULL, "Email" TEXT NOT NULL, "PasswordHash" TEXT NOT NULL, "Role" INTEGER NOT NULL, "IsActive" INTEGER NOT NULL, "IsDeleted" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)""",
                """CREATE TABLE "Categories" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "Name" TEXT NOT NULL, "Description" TEXT NULL, "IsDeleted" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)""",
                """CREATE TABLE "Suppliers" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "Name" TEXT NOT NULL, "ContactName" TEXT NULL, "Email" TEXT NULL, "Phone" TEXT NULL, "Address" TEXT NULL, "Notes" TEXT NULL, "IsActive" INTEGER NOT NULL, "IsDeleted" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)""",
                """CREATE TABLE "Products" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "CategoryId" INTEGER NOT NULL, "DefaultSupplierId" INTEGER NULL, "Sku" TEXT NOT NULL, "Name" TEXT NOT NULL, "Description" TEXT NULL, "CostPrice" NUMERIC NOT NULL, "SalePrice" NUMERIC NOT NULL, "TaxRate" NUMERIC NOT NULL, "StockQuantity" INTEGER NOT NULL, "MinStockThreshold" INTEGER NOT NULL, "RowVersion" INTEGER NOT NULL DEFAULT 0, "IsActive" INTEGER NOT NULL, "IsDeleted" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)""",
                """CREATE TABLE "PurchaseOrders" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "SupplierId" INTEGER NOT NULL, "CreatedByUserId" INTEGER NOT NULL, "Status" INTEGER NOT NULL, "OrderedAt" TEXT NULL, "ReceivedAt" TEXT NULL, "Version" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)""",
                """CREATE TABLE "PurchaseOrderItems" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "PurchaseOrderId" INTEGER NOT NULL, "ProductId" INTEGER NOT NULL, "Quantity" INTEGER NOT NULL, "UnitCost" NUMERIC NOT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)""",
                """CREATE TABLE "StockMovements" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "ProductId" INTEGER NOT NULL, "CreatedByUserId" INTEGER NOT NULL, "Type" INTEGER NOT NULL, "QuantityChange" INTEGER NOT NULL, "ReferenceType" TEXT NULL, "ReferenceId" INTEGER NULL, "Note" TEXT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)""",
                """CREATE TABLE "AuditLogs" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "UserId" INTEGER NULL, "Timestamp" TEXT NOT NULL, "Action" INTEGER NOT NULL, "EntityName" TEXT NOT NULL, "EntityId" TEXT NOT NULL, "OldValues" TEXT NULL, "NewValues" TEXT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)""",
                """CREATE TABLE "LowStockAlerts" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "ProductId" INTEGER NOT NULL, "DetectedAt" TEXT NOT NULL, "SentAt" TEXT NULL, "LastAttemptAt" TEXT NULL, "NextAttemptAt" TEXT NOT NULL, "ResolvedAt" TEXT NULL, "DeliveryAttempts" INTEGER NOT NULL, "LastError" TEXT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL)"""
            ];
            foreach (var statement in statements)
                await db.Database.ExecuteSqlRawAsync(statement);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserIdValue { get; set; }
        public int? UserId => UserIdValue;
        public string? Role => "Manager";
    }

}
