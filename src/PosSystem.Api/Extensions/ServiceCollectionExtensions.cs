using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using PosSystem.Api.Controllers;
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
        services.AddScoped<IClaimsTransformation, AuthorizationStateClaimsTransformation>();
        services.AddSingleton<ILoginAttemptLimiter, LoginAttemptLimiter>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddOptions<EmailOptions>()
            .Bind(config.GetSection(EmailOptions.SectionName))
            .Validate(EmailOptions.IsValid,
                "Enabled email requires valid SMTP host, port, sender address, TLS mode, and matching SMTP credentials.")
            .ValidateOnStart();
        services.AddTransient<IEmailSender, SmtpEmailSender>();
        services.AddTransient<LowStockEmailJob>();
        services.AddTransient<LowStockAlertDispatcher>();
        services.AddTransient<DailySalesReportJob>();
        services.AddScoped<ILowStockNotifier, HangfireLowStockNotifier>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<ProductImageStorage>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<ISaleReturnService, SaleReturnService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IHistoryService, HistoryService>();
        services.AddScoped<IExcelReportService, ExcelReportService>();
        services.AddScoped<IStaffService, StaffService>();
        return services;
    }
}
