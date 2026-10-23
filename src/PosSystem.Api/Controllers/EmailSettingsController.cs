using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Infrastructure.Services;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/email-settings"), Authorize(Roles = "Admin,Manager")]
public sealed class EmailSettingsController(
    IAppDbContext db,
    IOptions<EmailOptions> emailOptions,
    IEmailSender sender,
    ILogger<EmailSettingsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EmailSettingsDto>> Get(CancellationToken ct)
    {
        var values = await db.Settings.AsNoTracking()
            .Where(setting => setting.SettingKey == "ManagerEmail" || setting.SettingKey == "AlertsEnabled")
            .ToDictionaryAsync(setting => setting.SettingKey, setting => setting.Value, ct);
        return Ok(new EmailSettingsDto(
            emailOptions.Value.Enabled,
            values.GetValueOrDefault("ManagerEmail"),
            bool.TryParse(values.GetValueOrDefault("AlertsEnabled"), out var alertsEnabled) && alertsEnabled));
    }

    [HttpPut]
    public async Task<ActionResult<EmailSettingsDto>> Update(UpdateEmailSettingsRequest request, CancellationToken ct)
    {
        var managerEmail = await db.Settings.FirstOrDefaultAsync(
            setting => setting.SettingKey == "ManagerEmail", ct);
        var alertsEnabled = await db.Settings.FirstOrDefaultAsync(
            setting => setting.SettingKey == "AlertsEnabled", ct);
        if (managerEmail is null || alertsEnabled is null)
            throw new InvalidOperationException("Email settings are missing from the database seed.");

        managerEmail.Value = request.ManagerEmail.Trim();
        alertsEnabled.Value = request.AlertsEnabled.ToString();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Updated email recipient and low-stock alert preference.");
        return await Get(ct);
    }

    [HttpPost("test"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SendTest(CancellationToken ct)
    {
        if (!emailOptions.Value.Enabled)
            throw new BadRequestException("SMTP delivery is disabled. Configure Email__Enabled and valid SMTP settings on the server.");

        var recipient = await db.Settings.AsNoTracking()
            .Where(setting => setting.SettingKey == "ManagerEmail")
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(recipient))
            throw new BadRequestException("Save a valid manager email address before sending a test message.");

        await sender.SendAsync(recipient, "POS System SMTP test", "<p>Your POS System SMTP configuration is working.</p>", ct);
        logger.LogInformation("Sent SMTP test message to the configured manager recipient.");
        return Ok(new { message = "Test email sent to the configured manager address." });
    }
}
