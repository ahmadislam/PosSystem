using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PosSystem.Api.Controllers;
using PosSystem.Api.Extensions;

namespace PosSystem.Tests;

public class TrustedProxyTests
{
    [Fact]
    public async Task Forwarding_is_disabled_until_explicitly_configured()
    {
        var options = Configure();
        var context = Request("127.0.0.1", "203.0.113.10");
        await Forward(context, options);

        Assert.Equal(IPAddress.Loopback, context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task Trusted_proxy_supplies_client_address_and_scheme_but_cannot_replace_host()
    {
        var context = Request("10.10.0.5", "203.0.113.10");
        context.Request.Headers["X-Forwarded-Host"] = "attacker.test";
        await Forward(context, Configure(new()
        {
            ["Enabled"] = "true", ["KnownProxies:0"] = "10.10.0.5"
        }));

        Assert.Equal(IPAddress.Parse("203.0.113.10"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal("api.example.com", context.Request.Host.Value);
        Assert.True(AuthOriginPolicy.IsAllowed("https://api.example.com", [],
            $"{context.Request.Scheme}://{context.Request.Host}"));
    }

    [Fact]
    public async Task Untrusted_client_cannot_spoof_address_or_scheme()
    {
        var context = Request("198.51.100.20", "203.0.113.10");
        await Forward(context, Configure(new()
        {
            ["Enabled"] = "true", ["KnownProxies:0"] = "10.10.0.5"
        }));

        Assert.Equal(IPAddress.Parse("198.51.100.20"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
        Assert.False(AuthOriginPolicy.IsAllowed("https://api.example.com", [],
            $"{context.Request.Scheme}://{context.Request.Host}"));
    }

    [Fact]
    public async Task Trusted_network_is_supported_with_a_bounded_forwarding_chain()
    {
        var context = Request("10.10.0.5", "203.0.113.99, 192.0.2.40");
        await Forward(context, Configure(new()
        {
            ["Enabled"] = "true", ["KnownNetworks:0"] = "10.10.0.0/24", ["ForwardLimit"] = "1"
        }));

        Assert.Equal(IPAddress.Parse("192.0.2.40"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Trusted_forwarding_keeps_login_limits_separate_for_different_clients()
    {
        var options = Configure(new() { ["Enabled"] = "true", ["KnownProxies:0"] = "10.10.0.5" });
        var first = Request("10.10.0.5", "203.0.113.10");
        var second = Request("10.10.0.5", "203.0.113.11");
        await Forward(first, options);
        await Forward(second, options);
        var limiter = new LoginAttemptLimiter();
        for (var i = 0; i < 5; i++)
            limiter.RecordFailure(first.Connection.RemoteIpAddress!.ToString(), "cashier");

        Assert.NotNull(limiter.GetRetryAfter(first.Connection.RemoteIpAddress!.ToString(), "cashier"));
        Assert.Null(limiter.GetRetryAfter(second.Connection.RemoteIpAddress!.ToString(), "cashier"));
    }

    [Theory]
    [InlineData(null, null, "1")]
    [InlineData("not-an-address", null, "1")]
    [InlineData("0.0.0.0", null, "1")]
    [InlineData(null, "0.0.0.0/0", "1")]
    [InlineData(null, "::/0", "1")]
    [InlineData(null, "not-a-network", "1")]
    [InlineData("10.10.0.5", null, "0")]
    [InlineData("10.10.0.5", null, "11")]
    public void Invalid_or_unbounded_trust_configuration_fails_closed(string? proxy, string? network, string limit)
    {
        var values = new Dictionary<string, string?> { ["Enabled"] = "true", ["ForwardLimit"] = limit };
        if (proxy is not null) values["KnownProxies:0"] = proxy;
        if (network is not null) values["KnownNetworks:0"] = network;
        Assert.Throws<InvalidOperationException>(() => Configure(values));
    }

    private static ForwardedHeadersOptions Configure(Dictionary<string, string?>? values = null) =>
        TrustedProxyConfiguration.Create(new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? []).Build());

    private static DefaultHttpContext Request(string peer, string forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("api.example.com");
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        return context;
    }

    private static Task Forward(HttpContext context, ForwardedHeadersOptions options) =>
        new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            Options.Create(options)).Invoke(context);
}
