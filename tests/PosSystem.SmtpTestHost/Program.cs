using System.Text.Json;
using Microsoft.Extensions.Options;
using PosSystem.Infrastructure.Services;

// Separate process so SSL_CERT_FILE affects only this test SMTP client. The
// production sender uses its unchanged, normal certificate validation path.
var options = JsonSerializer.Deserialize<EmailOptions>(await File.ReadAllTextAsync(args.Single()))
              ?? throw new InvalidOperationException("Missing test SMTP options.");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
await new SmtpEmailSender(Options.Create(options)).SendAsync("manager@example.test",
    "POS SMTP integration", "<p>Verified transport</p>", timeout.Token);
Console.WriteLine("SMTP delivery completed.");
