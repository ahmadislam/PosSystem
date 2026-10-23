using MailKit.Security;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MimeKit;
using MailKit.Net.Smtp;

namespace PosSystem.Infrastructure.Services;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
    public string SenderName { get; set; } = "POS System";
    public bool UseStartTls { get; set; } = true;
    public bool UseSslOnConnect { get; set; }
    public int TimeoutSeconds { get; set; } = 15;

    public static bool IsValid(EmailOptions options) =>
        !options.Enabled ||
        (!string.IsNullOrWhiteSpace(options.SmtpHost)
         && options.SmtpPort is > 0 and <= 65535
         && MailboxAddress.TryParse(options.SenderAddress, out _)
         && options.UseStartTls != options.UseSslOnConnect
         && (string.IsNullOrWhiteSpace(options.Username) == string.IsNullOrWhiteSpace(options.Password)));
}

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken ct = default);
}

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly ILogger<SmtpEmailSender> logger;
    private readonly IOptions<EmailOptions> options;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        this.options = options;
        this.logger = logger;
    }

    public async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken ct = default)
    {
        var settings = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        var security = settings.UseSslOnConnect
            ? SecureSocketOptions.SslOnConnect
            : settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

        using var client = new SmtpClient();
        client.Timeout = Math.Clamp(settings.TimeoutSeconds, 5, 120) * 1000;
        try
        {
            logger.LogInformation("Connecting to SMTP server {Host}:{Port} using {Security}.", settings.SmtpHost, settings.SmtpPort, security);
            await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, security, ct);
            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                logger.LogInformation("Authenticating with SMTP server {Host}.", settings.SmtpHost);
                await client.AuthenticateAsync(settings.Username, settings.Password, ct);
            }
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            logger.LogInformation("SMTP message sent to {Recipient}.", recipient);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMTP delivery failed to {Recipient} via {Host}:{Port}.", recipient, settings.SmtpHost, settings.SmtpPort);
            throw;
        }
    }
}
