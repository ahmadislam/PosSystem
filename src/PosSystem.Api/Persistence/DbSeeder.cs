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
        const string marker = "DemoDataSeeded";
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

        if (!await db.Customers.AnyAsync(c => c.Email == "customer@example.test" && !c.IsDeleted))
            db.Customers.Add(new Customer { Name = "Demo Customer", Email = "customer@example.test", Phone = "+1 555 010 0200", Address = "1 Example Road" });
        if (!await db.Coupons.AnyAsync(c => c.Code == "WELCOME10"))
            db.Coupons.Add(new Coupon { Code = "WELCOME10", Name = "Welcome ten percent", IsPercentage = true, Value = 10m, StartsAt = DateTime.UtcNow.AddMinutes(-5), EndsAt = DateTime.UtcNow.AddDays(30) });
        db.Settings.Add(new Setting { SettingKey = marker, Value = DateTime.UtcNow.ToString("O") });
        await db.SaveChangesAsync();
    }

    private static async Task AddProductAsync(AppDbContext db, string sku, string name, string description, int categoryId, int supplierId, decimal cost, decimal price, decimal tax, int stock, int minimum)
    {
        if (await db.Products.AnyAsync(p => p.Sku == sku)) return;
        db.Products.Add(new Product { Sku = sku, Name = name, Description = description, CategoryId = categoryId, DefaultSupplierId = supplierId, CostPrice = cost, SalePrice = price, TaxRate = tax, StockQuantity = stock, MinStockThreshold = minimum, IsActive = true });
    }
}
