using System.Diagnostics;
using System.Text.Json;
using MimeKit;

namespace PosSystem.IntegrationTests;

public sealed class SmtpDeliveryTests
{
    [Fact]
    public async Task Real_sender_delivers_over_verified_TLS_to_local_SMTP_capture()
    {
        await using var smtp = new SmtpCapture();
        var result = await SendInIsolatedProcess(smtp, trustTestAuthority: true);
        Assert.True(result.ExitCode == 0, result.Error);
        var message = await smtp.NextMessageAsync();
        Assert.Equal("POS SMTP integration", message.Subject);
        Assert.Equal("manager@example.test", Assert.IsType<MailboxAddress>(Assert.Single(message.To)).Address);
        Assert.Equal("pos@example.test", Assert.IsType<MailboxAddress>(Assert.Single(message.From)).Address);
        Assert.Contains("Verified transport", message.HtmlBody);
    }

    [Fact]
    public async Task Unknown_TLS_authority_is_rejected_without_delivering_a_message()
    {
        await using var smtp = new SmtpCapture();
        var result = await SendInIsolatedProcess(smtp, trustTestAuthority: false);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SslHandshakeException", result.Error);
        Assert.Equal(0, smtp.MessageCount);
    }

    [Fact]
    public async Task SMTP_temporary_failure_is_reported_to_the_caller()
    {
        await using var smtp = new SmtpCapture { RejectMessages = true };
        var result = await SendInIsolatedProcess(smtp, trustTestAuthority: true);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.Error.Contains("SmtpCommandException", StringComparison.Ordinal), result.Error);
        Assert.Equal(0, smtp.MessageCount);
    }

    private static async Task<(int ExitCode, string Error)> SendInIsolatedProcess(SmtpCapture smtp, bool trustTestAuthority)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PosSystem.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        var host = Path.Combine(root.FullName, "tests", "PosSystem.SmtpTestHost", "bin", configuration, "net10.0", "PosSystem.SmtpTestHost.dll");
        Assert.True(File.Exists(host), "Build PosSystem.SmtpTestHost before running SMTP integration tests.");
        var optionsFile = Path.Combine(Path.GetDirectoryName(smtp.CaFilePath)!, "smtp-options.json");
        await File.WriteAllTextAsync(optionsFile, JsonSerializer.Serialize(smtp.Options));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(host);
        start.ArgumentList.Add(optionsFile);
        if (trustTestAuthority) start.Environment["SSL_CERT_FILE"] = smtp.CaFilePath;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start SMTP test host.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
