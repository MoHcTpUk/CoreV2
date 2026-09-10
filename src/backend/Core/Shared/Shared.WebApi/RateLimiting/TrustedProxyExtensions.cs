using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;

namespace Shared.Api.RateLimiting;

/// <summary>
///     Makes the real client address reach the rate limiter when the app runs behind a reverse proxy.
///     Both web hosts in this repository are published only through Traefik on the <c>infra_traefik</c>
///     overlay and expose no ports of their own, so without this every external caller shares Traefik's
///     address and any IP-keyed limit degenerates into a single bucket for the whole internet.
/// </summary>
public static class TrustedProxyExtensions
{
    public static IServiceCollection AddTrustedProxy(this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(TrustedProxyOptions.SectionName).Get<TrustedProxyOptions>()
                      ?? new TrustedProxyOptions();

        // A trust list is what makes ForwardedHeadersMiddleware do anything: its defaults cover loopback
        // only, so an empty list would leave it silently inert and hand back today's single bucket.
        // Fail at startup instead of discovering that from a graph of 429s.
        if (options.ClientIpSource == ClientIpSource.ForwardedHeader && !options.UsesForwardedHeader)
            throw new InvalidOperationException(
                $"'{TrustedProxyOptions.SectionName}:{nameof(TrustedProxyOptions.ClientIpSource)}' is " +
                $"'{nameof(ClientIpSource.ForwardedHeader)}' but neither " +
                $"'{nameof(TrustedProxyOptions.KnownProxies)}' nor '{nameof(TrustedProxyOptions.KnownNetworks)}' " +
                "lists an entry. Add the address or subnet the reverse proxy connects from.");

        services.AddSingleton(options);

        if (!options.UsesForwardedHeader) return services;

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = options.ForwardLimit;

            // The defaults trust loopback. Replace rather than extend them: the configured list is meant
            // to be the whole trust chain, and an unnoticed extra entry is an unnoticed spoofing path.
            forwarded.KnownProxies.Clear();
            forwarded.KnownNetworks.Clear();

            foreach (var proxy in options.KnownProxies)
                if (IPAddress.TryParse(proxy, out var address))
                    forwarded.KnownProxies.Add(address);

            foreach (var network in options.KnownNetworks)
                if (TryParseNetwork(network, out var prefix, out var prefixLength))
                    forwarded.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
        });

        return services;
    }

    /// <summary>
    ///     Registers <c>UseForwardedHeaders</c>. Must be the first middleware: everything downstream that
    ///     reads the client address — the rate limiter above all — has to see the rewritten value.
    /// </summary>
    public static IApplicationBuilder UseTrustedProxy(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<TrustedProxyOptions>();

        if (options.UsesForwardedHeader) return app.UseForwardedHeaders();

        if (options.ClientIpSource == ClientIpSource.Unknown)
            app.ApplicationServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(TrustedProxyExtensions))
                .LogWarning(
                    "'{Section}:{Key}' is not configured, so IP-keyed rate limits are disabled and only " +
                    "per-caller partitions apply. Set it to '{Direct}' when the app is exposed directly, " +
                    "or to '{Proxied}' with a trust list when it runs behind a reverse proxy.",
                    TrustedProxyOptions.SectionName,
                    nameof(TrustedProxyOptions.ClientIpSource),
                    nameof(ClientIpSource.ConnectionRemoteIp),
                    nameof(ClientIpSource.ForwardedHeader));

        return app;
    }

    private static bool TryParseNetwork(string value, out IPAddress prefix, out int prefixLength)
    {
        prefix = IPAddress.None;
        prefixLength = 0;

        var separator = value.IndexOf('/');
        if (separator <= 0) return false;

        return IPAddress.TryParse(value[..separator], out prefix!) &&
               int.TryParse(value[(separator + 1)..], out prefixLength) &&
               prefixLength >= 0 &&
               prefixLength <= (prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32);
    }
}
