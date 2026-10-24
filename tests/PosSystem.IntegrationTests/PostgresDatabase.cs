using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;
using PosSystem.Infrastructure.Persistence;
using PosSystem.Infrastructure.Services;

namespace PosSystem.IntegrationTests;

public sealed class PgFactAttribute : FactAttribute
{
    public PgFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POS_TEST_POSTGRES")) &&
            Environment.GetEnvironmentVariable("POS_REQUIRE_POSTGRES_TESTS") != "true")
            Skip = "Set POS_TEST_POSTGRES to a disposable PostgreSQL server connection with CREATE DATABASE permission.";
    }
}

public sealed class PgTheoryAttribute : TheoryAttribute
{
    public PgTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POS_TEST_POSTGRES")) &&
            Environment.GetEnvironmentVariable("POS_REQUIRE_POSTGRES_TESTS") != "true")
            Skip = "Set POS_TEST_POSTGRES to a disposable PostgreSQL server connection with CREATE DATABASE permission.";
    }
}

internal sealed class TestUser : ICurrentUser
{
    public int? UserId { get; set; }
    public string? Role { get; set; } = "Admin";
}

internal sealed class PostgresDatabase : IAsyncDisposable
{
    private readonly string adminConnection;
    internal string ConnectionString { get; }
    internal TestUser User { get; } = new();

    private PostgresDatabase(string adminConnection, string connectionString)
    {
        this.adminConnection = adminConnection;
        ConnectionString = connectionString;
    }

    internal static async Task<PostgresDatabase> CreateAsync(bool migrate = true)
    {
        var configured = Environment.GetEnvironmentVariable("POS_TEST_POSTGRES")
            ?? throw new InvalidOperationException("POS_TEST_POSTGRES must be set; integration tests never use an implicit developer database.");
        var admin = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Pooling = false };
        var name = "pos_it_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        var target = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
        var database = new PostgresDatabase(admin.ConnectionString, target.ConnectionString);
        try
        {
            if (migrate)
            {
                await using var db = database.Open();
                await db.Database.MigrateAsync();
            }
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    internal AppDbContext Open(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString);
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        return new AppDbContext(options.Options, User);
    }

    internal async Task<Product> SeedAsync(decimal price = 10m, int stock = 20)
    {
        await using var db = Open();
        var user = new User { Username = "integration-admin", Email = "admin@example.test", Role = UserRole.Admin, IsActive = true };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Integration-only-passphrase-73!");
        var category = new Category { Name = "Integration" };
        db.Users.Add(user);
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        User.UserId = user.Id;
        var product = new Product { Sku = "TEST-SKU", Name = "Test product", CategoryId = category.Id,
            SalePrice = price, CostPrice = 1m, StockQuantity = stock, MinStockThreshold = 2, IsActive = true };
        db.Products.Add(product);
        db.Settings.AddRange(new Setting { SettingKey = "ManagerEmail", Value = "manager@example.test" },
            new Setting { SettingKey = "AlertsEnabled", Value = "true" });
        await db.SaveChangesAsync();
        return product;
    }

    internal SaleService Sales(AppDbContext db) => new(db, User, new HangfireLowStockNotifier(db));
    internal SaleReturnService Returns(AppDbContext db) => new(db, User, new HangfireLowStockNotifier(db));
    internal ShiftService Shifts(AppDbContext db) => new(db, User);

    internal static CreateSaleRequest CashSale(int productId, int quantity = 1, decimal tender = 1000m) => new()
    {
        PaymentMethod = PaymentMethod.Cash, CashTendered = tender,
        Items = [new SaleLineRequest { ProductId = productId, Quantity = quantity }]
    };

    public async ValueTask DisposeAsync()
    {
        var name = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
        if (name is null || !name.StartsWith("pos_it_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to drop a database outside the integration-test namespace.");
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
