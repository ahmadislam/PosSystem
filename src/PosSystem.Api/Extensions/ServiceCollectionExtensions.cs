using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PosSystem.Api.Persistence;
using PosSystem.Application.Common;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Infrastructure.Persistence;
using PosSystem.Infrastructure.Services;

namespace PosSystem.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPosServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(
            config.GetConnectionString("Default"),
            n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<ILowStockNotifier, LoggingLowStockNotifier>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IDashboardService, DashboardService>();
        return services;
    }
}
