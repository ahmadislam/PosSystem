using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PosSystem.IntegrationTests;

internal sealed class ApiProcess : IAsyncDisposable
{
    private readonly Process process;
    private bool started;
    private readonly StringBuilder log = new();
    internal HttpClient Client { get; }
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const string Password = "Integration-only-passphrase-73!";

    private ApiProcess(Process process, int port)
    {
        this.process = process;
        Client = new HttpClient(new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30)
        };
        Client.DefaultRequestHeaders.Add("Origin", "http://localhost:4200");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
    }

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PosSystem.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run integration tests from a built repository checkout.");
    }

    internal static async Task<ApiProcess> StartAsync(PostgresDatabase database,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var apiDirectory = Path.Combine(RepositoryRoot(), "src", "PosSystem.Api");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        var dll = Path.Combine(apiDirectory, "bin", configuration, "net10.0", "PosSystem.Api.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Build the integration project before using --no-build.", dll);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = apiDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(dll);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["ConnectionStrings__Default"] = database.ConnectionString;
        start.Environment["Seed__AdminUsername"] = "admin";
        start.Environment["Seed__AdminPassword"] = Password;
        start.Environment["Seed__AdminEmail"] = "admin@example.test";
        start.Environment["DemoAccess__Enabled"] = "false";
        start.Environment["Jwt__Key"] = "integration-only-signing-key-with-more-than-thirty-two-characters";
        start.Environment["Cors__Origins__0"] = "http://localhost:4200";
        start.Environment["Email__Enabled"] = "false";
        start.Environment["Email__Username"] = "";
        start.Environment["Email__Password"] = "";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.EntityFrameworkCore"] = "Warning";
        if (settings is not null)
            foreach (var (key, value) in settings) start.Environment[key] = value;
        var process = new Process { StartInfo = start };
        var host = new ApiProcess(process, port);
        try
        {
            host.started = process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            while (!deadline.IsCancellationRequested)
            {
                if (process.HasExited) throw new InvalidOperationException("API exited before readiness. " + host.log);
                try
                {
                    using var response = await host.Client.GetAsync("/health", deadline.Token);
                    if (response.IsSuccessStatusCode && await response.Content.ReadAsStringAsync(deadline.Token) == "Healthy")
                        return host;
                }
                catch (HttpRequestException) { }
                await Task.Delay(100, deadline.Token);
            }
            throw new TimeoutException("API failed to become healthy. " + host.log);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    internal async Task<(string Token, string Cookie)> LoginAsync()
    {
        using var response = await Client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = Password });
        await EnsureSuccessAsync(response);
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.Contains("samesite=none", cookie.ToLowerInvariant());
        var token = auth.GetProperty("accessToken").GetString()!;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (token, cookie.Split(';')[0]);
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode,
            $"Expected successful response, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (started && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        process.Dispose();
    }
}
