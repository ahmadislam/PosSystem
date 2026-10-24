using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace PosSystem.Api.Extensions;

/// <summary>Forward only client address and scheme, and only from explicitly trusted peers.</summary>
public static class TrustedProxyConfiguration
{
    public static ForwardedHeadersOptions Create(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.None };
        // Remove framework loopback defaults: deployments must opt into their actual proxy peers.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        if (!configuration.GetValue<bool>("Enabled")) return options;

        options.ForwardLimit = configuration.GetValue("ForwardLimit", 1);
        if (options.ForwardLimit is < 1 or > 10)
            throw new InvalidOperationException("ReverseProxy:ForwardLimit must be between 1 and 10.");

        foreach (var raw in configuration.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(raw, out var address) ||
                address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("ReverseProxy:KnownProxies must contain explicit proxy IP addresses.");
            options.KnownProxies.Add(address);
        }
        foreach (var raw in configuration.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            if (!System.Net.IPNetwork.TryParse(raw, out var network) || network.PrefixLength == 0)
                throw new InvalidOperationException("ReverseProxy:KnownNetworks must contain trusted CIDR ranges, not all addresses.");
            options.KnownIPNetworks.Add(network);
        }
        if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
            throw new InvalidOperationException("Enabling ReverseProxy requires KnownProxies or KnownNetworks; never trust all clients.");

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        return options;
    }
}
