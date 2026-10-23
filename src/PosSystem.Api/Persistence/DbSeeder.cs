using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;
using PosSystem.Infrastructure.Persistence;

namespace PosSystem.Api.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            var admin = new User
            {
                Username = config["Seed:AdminUsername"] ?? "admin",
                Email = config["Seed:AdminEmail"] ?? "admin@example.com",
                Role = UserRole.Admin
            };
            admin.PasswordHash = hasher.HashPassword(admin, config["Seed:AdminPassword"] ?? "Admin@12345");
            db.Users.Add(admin);
        }

        if (!await db.Settings.AnyAsync())
        {
            db.Settings.AddRange(
                new Setting { SettingKey = "BusinessName", Value = "My Shop" },
                new Setting { SettingKey = "BusinessAddress", Value = "" },
                new Setting { SettingKey = "TaxNumber", Value = "" },
                new Setting { SettingKey = "InvoiceFooter", Value = "Thank you for shopping with us!" },
                new Setting { SettingKey = "ManagerEmail", Value = "" },
                new Setting { SettingKey = "AlertsEnabled", Value = "true" });
        }

        if (!await db.Categories.AnyAsync())
            db.Categories.Add(new Category { Name = "General" });

        await db.SaveChangesAsync();

        if (config.GetValue<bool>("Seed:DemoData"))
            await SeedDemoDataAsync(db);
    }

    private static async Task SeedDemoDataAsync(AppDbContext db)
    {
        const string marker = "DemoDataSeededV3";
        if (await db.Settings.AnyAsync(s => s.SettingKey == marker)) return;

        var beverages = await db.Categories.FirstOrDefaultAsync(c => c.Name == "Beverages" && !c.IsDeleted);
        if (beverages is null)
        {
            beverages = new Category { Name = "Beverages", Description = "Cold and hot drinks" };
            db.Categories.Add(beverages);
        }
        var snacks = await db.Categories.FirstOrDefaultAsync(c => c.Name == "Snacks" && !c.IsDeleted);
        if (snacks is null)
        {
            snacks = new Category { Name = "Snacks", Description = "Packaged snacks for the demo counter" };
            db.Categories.Add(snacks);
        }

        var foods = await db.Suppliers.FirstOrDefaultAsync(s => s.Name == "Demo Foods Ltd" && !s.IsDeleted);
        if (foods is null)
        {
            foods = new Supplier { Name = "Demo Foods Ltd", ContactName = "Aisha Khan", Email = "foods@example.test", Phone = "+1 555 010 0101", Address = "100 Demo Avenue", Notes = "Coffee and tea supplier" };
            db.Suppliers.Add(foods);
        }
        var sweets = await db.Suppliers.FirstOrDefaultAsync(s => s.Name == "Sweet Supply Co" && !s.IsDeleted);
        if (sweets is null)
        {
            sweets = new Supplier { Name = "Sweet Supply Co", ContactName = "Omar Ali", Email = "sweet@example.test", Phone = "+1 555 010 0102", Address = "200 Sample Street", Notes = "Snacks supplier" };
            db.Suppliers.Add(sweets);
        }
        await db.SaveChangesAsync();

        await AddProductAsync(db, "COF-250", "House Coffee 250g", "Arabica ground coffee", beverages.Id, foods.Id, 5m, 8.5m, 5m, 25, 5);
        await AddProductAsync(db, "TEA-100", "Green Tea 100 bags", "Caffeine-free demo tea", beverages.Id, foods.Id, 3m, 6m, 5m, 12, 4);
        await AddProductAsync(db, "CHP-050", "Chocolate Bar", "50g chocolate bar", snacks.Id, sweets.Id, .5m, 1.25m, 0m, 40, 8);
        await AddProductAsync(db, "LOW-001", "Low Stock Sample", "Used to trigger an alert", snacks.Id, sweets.Id, 1m, 2m, 0m, 2, 5);
        await AddProductAsync(db, "COF-1KG", "House Coffee 1kg", "Bulk Arabica coffee beans", beverages.Id, foods.Id, 14m, 24.99m, 5m, 18, 4);
        await AddProductAsync(db, "TEA-BLK", "Breakfast Tea", "Black tea bags, 80 count", beverages.Id, foods.Id, 2.25m, 4.5m, 5m, 30, 6);
        await AddProductAsync(db, "JUI-500", "Orange Juice", "500ml chilled juice", beverages.Id, foods.Id, 1.1m, 2.75m, 5m, 16, 5);
        await AddProductAsync(db, "WAT-500", "Spring Water", "500ml bottled water", beverages.Id, foods.Id, .25m, 1m, 0m, 60, 12);
        await AddProductAsync(db, "CHP-100", "Dark Chocolate", "100g dark chocolate bar", snacks.Id, sweets.Id, 1m, 2.5m, 0m, 22, 5);
        await AddProductAsync(db, "NUT-200", "Roasted Nuts", "200g salted mixed nuts", snacks.Id, sweets.Id, 2m, 4.25m, 0m, 14, 4);
        await AddProductAsync(db, "CKE-006", "Cookie Pack", "Six assorted cookies", snacks.Id, sweets.Id, 1.25m, 3m, 0m, 3, 5);
        await AddProductAsync(db, "GFT-001", "Gift Bag", "Reusable paper gift bag", snacks.Id, sweets.Id, .4m, 1.5m, 0m, 9, 3);

        if (!await db.Customers.AnyAsync(c => c.Email == "customer@example.test" && !c.IsDeleted))
            db.Customers.Add(new Customer { Name = "Demo Customer", Email = "customer@example.test", Phone = "+1 555 010 0200", Address = "1 Example Road" });
        if (!await db.Customers.AnyAsync(c => c.Email == "loyal@example.test" && !c.IsDeleted))
            db.Customers.Add(new Customer { Name = "Loyalty Member", Email = "loyal@example.test", Phone = "+1 555 010 0201", Address = "2 Sample Lane", LoyaltyPoints = 250 });
        if (!await db.Customers.AnyAsync(c => c.Email == "business@example.test" && !c.IsDeleted))
            db.Customers.Add(new Customer { Name = "Business Customer", Email = "business@example.test", Phone = "+1 555 010 0202", Address = "3 Market Street" });
        if (!await db.Customers.AnyAsync(c => c.Email == "inactive@example.test" && !c.IsDeleted))
            db.Customers.Add(new Customer { Name = "Inactive Customer", Email = "inactive@example.test", Phone = "+1 555 010 0203", Address = "4 Archive Road", IsActive = false });
        if (!await db.Coupons.AnyAsync(c => c.Code == "WELCOME10"))
            db.Coupons.Add(new Coupon { Code = "WELCOME10", Name = "Welcome ten percent", IsPercentage = true, Value = 10m, StartsAt = DateTime.UtcNow.AddMinutes(-5), EndsAt = DateTime.UtcNow.AddDays(30) });
        if (!await db.Coupons.AnyAsync(c => c.Code == "SAVE5"))
            db.Coupons.Add(new Coupon { Code = "SAVE5", Name = "Five currency units off", Value = 5m, StartsAt = DateTime.UtcNow.AddMinutes(-5), EndsAt = DateTime.UtcNow.AddDays(60), MaxRedemptions = 50 });
        if (!await db.Coupons.AnyAsync(c => c.Code == "EXPIRED"))
            db.Coupons.Add(new Coupon { Code = "EXPIRED", Name = "Expired demo coupon", IsPercentage = true, Value = 15m, StartsAt = DateTime.UtcNow.AddDays(-60), EndsAt = DateTime.UtcNow.AddDays(-30), IsActive = false });
        if (!await db.Coupons.AnyAsync(c => c.Code == "HALFPRICE"))
            db.Coupons.Add(new Coupon { Code = "HALFPRICE", Name = "Half price test", IsPercentage = true, Value = 50m, StartsAt = DateTime.UtcNow.AddMinutes(-5), EndsAt = DateTime.UtcNow.AddDays(7), MaxRedemptions = 10 });
        db.Settings.Add(new Setting { SettingKey = marker, Value = DateTime.UtcNow.ToString("O") });
        await db.SaveChangesAsync();

        await SeedDemoTransactionsAsync(db);
    }

    private static async Task SeedDemoTransactionsAsync(AppDbContext db)
    {
        var admin = await db.Users.OrderBy(u => u.Id).FirstAsync();
        var products = await db.Products.Where(p => p.Sku == "COF-250" || p.Sku == "TEA-100" || p.Sku == "CHP-050" || p.Sku == "WAT-500").ToDictionaryAsync(p => p.Sku);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == "customer@example.test");
        if (products.Count < 4 || await db.Sales.AnyAsync(s => s.InvoiceNumber.StartsWith("DEMO-"))) return;

        for (var index = 0; index < 6; index++)
        {
            var saleDate = DateTime.UtcNow.Date.AddDays(-index).AddHours(10 + index);
            var coffee = products["COF-250"];
            var tea = products["TEA-100"];
            var water = products["WAT-500"];
            var coffeeLine = new SaleItem { ProductId = coffee.Id, Sku = coffee.Sku, ProductName = coffee.Name, Quantity = index % 2 + 1, UnitPrice = coffee.SalePrice, LineTotal = coffee.SalePrice * (index % 2 + 1) };
            var teaLine = new SaleItem { ProductId = tea.Id, Sku = tea.Sku, ProductName = tea.Name, Quantity = 1, UnitPrice = tea.SalePrice, LineTotal = tea.SalePrice };
            var waterLine = new SaleItem { ProductId = water.Id, Sku = water.Sku, ProductName = water.Name, Quantity = index % 3 + 1, UnitPrice = water.SalePrice, LineTotal = water.SalePrice * (index % 3 + 1) };
            var subtotal = coffeeLine.LineTotal + teaLine.LineTotal + waterLine.LineTotal;
            var shift = new CashierShift { CashierId = admin.Id, StartTime = saleDate.AddHours(-1), EndTime = saleDate.AddHours(1), OpeningCash = 100m, ExpectedCash = 100m + subtotal, ClosingCash = 100m + subtotal, CashVariance = 0m, Status = ShiftStatus.Closed };
            var sale = new Sale { InvoiceNumber = $"DEMO-{index + 1:0000}", CashierId = admin.Id, Shift = shift, CustomerId = index % 2 == 0 ? customer?.Id : null, SaleDate = saleDate, Subtotal = subtotal, TotalAmount = subtotal, PaymentMethod = (index % 3) switch { 0 => PaymentMethod.Cash, 1 => PaymentMethod.Card, _ => PaymentMethod.Online }, CashTendered = index % 3 == 0 ? subtotal + 5m : null, ChangeDue = index % 3 == 0 ? 5m : null, Items = [coffeeLine, teaLine, waterLine] };
            db.Sales.Add(sale);
            db.AuditLogs.Add(new AuditLog { UserId = admin.Id, Timestamp = saleDate, Action = AuditAction.Create, EntityName = "Sale", EntityId = sale.InvoiceNumber, NewValues = "{\"source\":\"demo-seed\"}" });
        }
        await db.SaveChangesAsync();
    }

    private static async Task AddProductAsync(AppDbContext db, string sku, string name, string description, int categoryId, int supplierId, decimal cost, decimal price, decimal tax, int stock, int minimum)
    {
        if (await db.Products.AnyAsync(p => p.Sku == sku)) return;
        db.Products.Add(new Product { Sku = sku, Name = name, Description = description, CategoryId = categoryId, DefaultSupplierId = supplierId, CostPrice = cost, SalePrice = price, TaxRate = tax, StockQuantity = stock, MinStockThreshold = minimum, IsActive = true });
    }
}
