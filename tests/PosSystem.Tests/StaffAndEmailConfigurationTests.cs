using PosSystem.Application.Common;
using PosSystem.Application.Services;
using PosSystem.Domain.Enums;
using PosSystem.Infrastructure.Services;

namespace PosSystem.Tests;

public class StaffAndEmailConfigurationTests
{
    [Fact]
    public void Self_cannot_be_demoted_or_deactivated()
    {
        Assert.Throws<BadRequestException>(() =>
            StaffAccessPolicy.EnsureCanUpdateSelf(7, 7, UserRole.Manager, true));
        Assert.Throws<BadRequestException>(() =>
            StaffAccessPolicy.EnsureCanUpdateSelf(7, 7, UserRole.Admin, false));
    }

    [Fact]
    public void Self_can_keep_admin_access()
    {
        StaffAccessPolicy.EnsureCanUpdateSelf(7, 7, UserRole.Admin, true);
    }

    [Fact]
    public void Last_active_admin_cannot_be_demoted_or_disabled()
    {
        Assert.Throws<ConflictException>(() =>
            StaffAccessPolicy.EnsureActiveAdminRemains(true, 1, UserRole.Manager, true));
        Assert.Throws<ConflictException>(() =>
            StaffAccessPolicy.EnsureActiveAdminRemains(true, 1, UserRole.Admin, false));
    }

    [Fact]
    public void Admin_change_is_allowed_when_another_active_admin_remains()
    {
        StaffAccessPolicy.EnsureActiveAdminRemains(true, 2, UserRole.Manager, true);
        StaffAccessPolicy.EnsureActiveAdminRemains(true, 2, UserRole.Admin, false);
    }

    [Fact]
    public void Disabled_email_does_not_require_smtp_settings()
    {
        Assert.True(EmailOptions.IsValid(new EmailOptions()));
    }

    [Fact]
    public void Enabled_smtp_accepts_starttls_with_complete_credentials()
    {
        Assert.True(EmailOptions.IsValid(new EmailOptions
        {
            Enabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            SenderAddress = "pos@example.com",
            Username = "smtp-user",
            Password = "smtp-password",
            UseStartTls = true
        }));
    }

    [Fact]
    public void Enabled_smtp_rejects_missing_or_invalid_configuration()
    {
        Assert.False(EmailOptions.IsValid(new EmailOptions { Enabled = true }));
        Assert.False(EmailOptions.IsValid(new EmailOptions
        {
            Enabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 70000,
            SenderAddress = "not-an-email"
        }));
    }

    [Fact]
    public void Enabled_smtp_rejects_conflicting_tls_modes_and_partial_credentials()
    {
        var conflictingTls = new EmailOptions
        {
            Enabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            SenderAddress = "pos@example.com",
            UseStartTls = true,
            UseSslOnConnect = true
        };
        var incompleteCredentials = new EmailOptions
        {
            Enabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            SenderAddress = "pos@example.com",
            Username = "smtp-user",
            UseStartTls = true
        };

        Assert.False(EmailOptions.IsValid(conflictingTls));
        Assert.False(EmailOptions.IsValid(incompleteCredentials));
    }

    [Fact]
    public void Enabled_smtp_requires_encryption()
    {
        var noTls = new EmailOptions
        {
            Enabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 25,
            SenderAddress = "pos@example.com",
            UseStartTls = false,
            UseSslOnConnect = false
        };

        Assert.False(EmailOptions.IsValid(noTls));
    }
}
