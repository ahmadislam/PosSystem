using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using MimeKit;
using PosSystem.Infrastructure.Services;

namespace PosSystem.IntegrationTests;

/// <summary>A loopback SMTP sink with a private test CA. Clients use CaFilePath through
/// SSL_CERT_FILE in their own process; production certificate verification stays enabled.</summary>
internal sealed class SmtpCapture : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly TcpListener crlListener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<MimeMessage> messages = Channel.CreateUnbounded<MimeMessage>();
    private readonly List<Task> clients = [];
    private readonly X509Certificate2 certificate;
    private readonly string directory;
    private readonly Task accepting;
    private readonly Task servingCrl;
    private readonly byte[] revocationList;

    public string CaFilePath { get; }
    public bool RejectMessages { get; set; }
    public int MessageCount { get; private set; }
    public EmailOptions Options { get; }
    public static Task<SmtpCapture> StartAsync() => Task.FromResult(new SmtpCapture());
    public IReadOnlyDictionary<string, string?> EnvironmentSettings => new Dictionary<string, string?>
    {
        ["SSL_CERT_FILE"] = CaFilePath,
        ["Email__Enabled"] = "true",
        ["Email__SmtpHost"] = Options.SmtpHost,
        ["Email__SmtpPort"] = Options.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Email__SenderAddress"] = Options.SenderAddress,
        ["Email__UseStartTls"] = "false",
        ["Email__UseSslOnConnect"] = "true"
    };

    public SmtpCapture()
    {
        directory = Path.Combine(Path.GetTempPath(), "pos-smtp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        crlListener.Start();
        var crlUrl = $"http://127.0.0.1:{((IPEndPoint)crlListener.LocalEndpoint).Port}/authority.crl";
        using var authorityKey = RSA.Create(2048);
        var authorityRequest = new CertificateRequest("CN=POS ephemeral SMTP test CA", authorityKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        authorityRequest.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([crlUrl]));
        using var authority = authorityRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(2));
        revocationList = new CertificateRevocationListBuilder().Build(authority, 1,
            DateTimeOffset.UtcNow.AddHours(1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1,
            DateTimeOffset.UtcNow.AddMinutes(-1));

        using var serverKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([crlUrl]));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var signed = request.Create(authority, DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        certificate = signed.CopyWithPrivateKey(serverKey);

        // Keep system trust as well as this fixture's root. The file contains public
        // certificates only and is supplied solely to isolated test child processes.
        CaFilePath = Path.Combine(directory, "ca-bundle.pem");
        var systemBundle = new[] { "/etc/ssl/certs/ca-certificates.crt", "/etc/pki/tls/certs/ca-bundle.crt" }
            .FirstOrDefault(File.Exists);
        File.WriteAllText(CaFilePath, (systemBundle is null ? "" : File.ReadAllText(systemBundle)) +
            "\n" + authority.ExportCertificatePem() + "\n");
        listener.Start();
        Options = new EmailOptions
        {
            Enabled = true, SmtpHost = "localhost", SmtpPort = ((IPEndPoint)listener.LocalEndpoint).Port,
            SenderAddress = "pos@example.test", SenderName = "POS integration", UseStartTls = false,
            UseSslOnConnect = true
        };
        accepting = AcceptAsync();
        servingCrl = ServeCrlAsync();
    }

    private async Task ServeCrlAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await crlListener.AcceptTcpClientAsync(stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/pkix-crl\r\nContent-Length: {revocationList.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, stop.Token);
                await stream.WriteAsync(revocationList, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
        catch (IOException) when (stop.IsCancellationRequested) { }
    }

    public async Task<MimeMessage> NextMessageAsync(TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        return await messages.Reader.ReadAsync(deadline.Token);
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                clients.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var ssl = new SslStream(client.GetStream()))
        {
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, stop.Token);
                using var reader = new StreamReader(ssl, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(ssl, new UTF8Encoding(false), leaveOpen: true)
                    { AutoFlush = true, NewLine = "\r\n" };
                await writer.WriteLineAsync("220 localhost POS test SMTP");
                while (!stop.IsCancellationRequested && await reader.ReadLineAsync(stop.Token) is { } line)
                {
                    var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                    switch (verb)
                    {
                        case "EHLO":
                            await writer.WriteLineAsync("250-localhost\r\n250-8BITMIME\r\n250 SIZE 10485760");
                            break;
                        case "HELO": case "MAIL": case "RCPT": case "RSET": case "NOOP":
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "DATA":
                            if (RejectMessages)
                            {
                                await writer.WriteLineAsync("451 Temporary test delivery failure");
                                break;
                            }
                            await writer.WriteLineAsync("354 End data with <CRLF>.<CRLF>");
                            var data = new StringBuilder();
                            while (await reader.ReadLineAsync(stop.Token) is { } body && body != ".")
                                data.Append(body.StartsWith("..", StringComparison.Ordinal) ? body[1..] : body).Append("\r\n");
                            using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes(data.ToString())))
                            {
                                var message = await MimeMessage.LoadAsync(bytes, stop.Token);
                                MessageCount++;
                                await messages.Writer.WriteAsync(message, stop.Token);
                            }
                            await writer.WriteLineAsync("250 Message accepted");
                            break;
                        case "QUIT":
                            await writer.WriteLineAsync("221 Goodbye");
                            return;
                        default:
                            await writer.WriteLineAsync("502 Unsupported test SMTP command");
                            break;
                    }
                }
            }
            catch (AuthenticationException) { /* The negative trust test rejects this CA. */ }
            catch (IOException) { /* Client rejected TLS or disconnected. */ }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener.Stop();
        crlListener.Stop();
        await accepting;
        await servingCrl;
        await Task.WhenAll(clients);
        messages.Writer.TryComplete();
        certificate.Dispose();
        stop.Dispose();
        Directory.Delete(directory, true);
    }
}
