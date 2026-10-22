using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PosSystem.Api.Persistence;
using PosSystem.Application.Common;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Infrastructure.Persistence;
using PosSystem.Infrastructure.Services;
using MimeKit;

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
        services.AddOptions<EmailOptions>()
            .Bind(config.GetSection(EmailOptions.SectionName))
            .Validate(options =>
                !options.Enabled ||
                (!string.IsNullOrWhiteSpace(options.SmtpHost)
                 && options.SmtpPort is > 0 and <= 65535
                 && MailboxAddress.TryParse(options.SenderAddress, out _)
                 && !(options.UseStartTls && options.UseSslOnConnect)
                 && (string.IsNullOrWhiteSpace(options.Username) == string.IsNullOrWhiteSpace(options.Password))),
                "Enabled email requires valid SMTP host, port, sender address, TLS mode, and matching SMTP credentials.")
            .ValidateOnStart();
        services.AddTransient<IEmailSender, SmtpEmailSender>();
        services.AddTransient<LowStockEmailJob>();
        services.AddTransient<DailySalesReportJob>();
        services.AddScoped<ILowStockNotifier, HangfireLowStockNotifier>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IDashboardService, DashboardService>();
        return services;
    }
}
