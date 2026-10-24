using System.Text;
using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PosSystem.Api.Extensions;
using PosSystem.Api.Middleware;
using PosSystem.Api.Persistence;
using PosSystem.Application.Common;
using PosSystem.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
var serveClient = ClientHosting.Configure(builder);
var forwardedHeaders = TrustedProxyConfiguration.Create(builder.Configuration.GetSection("ReverseProxy"));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
          ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
if (jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");
var demoAccess = builder.Configuration.GetSection("DemoAccess").Get<DemoAccessSettings>() ?? new DemoAccessSettings();
if (demoAccess.Enabled && string.IsNullOrWhiteSpace(demoAccess.Username))
    throw new InvalidOperationException("DemoAccess:Username must be configured when demo access is enabled.");

if (builder.Environment.IsProduction())
{
    if (jwt.Key == "CHANGE_ME_use_a_long_random_secret_of_at_least_32_chars")
        throw new InvalidOperationException("Set Jwt:Key to a unique random secret in Production.");
    var adminPassword = builder.Configuration["Seed:AdminPassword"];
    if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 12 || adminPassword == "Admin@12345")
        throw new InvalidOperationException("Set Seed:AdminPassword to a unique password of at least 12 characters in Production.");
}

var emailOptions = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
if (!EmailOptions.IsValid(emailOptions))
    throw new InvalidOperationException("Email SMTP configuration is invalid. Verify host, port, sender address, TLS settings, and SMTP credential pairing.");

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(demoAccess);
builder.Services.AddHttpContextAccessor();
builder.Services.AddPosServices(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration.");
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer(options => options.WorkerCount =
    Math.Clamp(builder.Configuration.GetValue("Hangfire:WorkerCount", 2), 1, 20));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;   // keep "sub" / "role" claim names as issued
    o.EventsType = typeof(AuthorizationStateJwtEvents);
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "name",
        RoleClaimType = "role"
    };
});
builder.Services.AddAuthorization();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "POS System API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        In = ParameterLocation.Header, Description = "Paste the accessToken returned by /api/auth/login."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

// Must precede authentication/limiting so only trusted proxies can supply the client IP/scheme.
app.UseForwardedHeaders(forwardedHeaders);
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSwagger();       // public on purpose: the live Swagger link goes on your CV
app.UseSwaggerUI();
app.UseCors("web");
if (serveClient)
{
    ClientHosting.UseClientFiles(app);
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
if (serveClient) ClientHosting.MapClientFallback(app);

await DbSeeder.SeedAsync(app.Services, app.Configuration);
var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate<LowStockAlertDispatcher>(
    "low-stock-alert-dispatch",
    job => job.DispatchAsync(CancellationToken.None),
    Cron.Minutely,
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
if (app.Configuration.GetValue<bool>("Email:Enabled"))
{
    recurringJobs.AddOrUpdate<DailySalesReportJob>(
        "daily-sales-summary",
        job => job.SendAsync(CancellationToken.None),
        Cron.Daily,
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    recurringJobs.AddOrUpdate<DailySalesReportJob>(
        "monthly-sales-summary",
        job => job.SendMonthlyAsync(CancellationToken.None),
        "15 0 1 * *",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
else
{
    recurringJobs.RemoveIfExists("daily-sales-summary");
    recurringJobs.RemoveIfExists("monthly-sales-summary");
    app.Logger.LogInformation("Email delivery is disabled; the daily sales report is not scheduled.");
}
app.Run();
