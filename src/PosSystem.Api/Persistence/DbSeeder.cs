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
    }
}
