namespace Shared.Api.RateLimiting;

/// <summary>
///     Where the client IP that the rate limiter partitions on comes from, bound from the
///     <c>TrustedProxy</c> configuration section.
///     This has to be declared rather than detected: an app behind a reverse proxy and an app exposed
///     directly both see a plausible-looking <c>Connection.RemoteIpAddress</c>, but only in the second
///     case is it the caller's. Guessing wrong in one direction lets anyone forge a partition key, and in
///     the other collapses every caller on Earth into one bucket.
/// </summary>
public class TrustedProxyOptions
{
    public const string SectionName = "TrustedProxy";

    public ClientIpSource ClientIpSource { get; set; } = ClientIpSource.Unknown;

    /// <summary>Individual proxy addresses, e.g. a pinned Traefik task address.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>CIDR networks a trusted proxy may connect from, e.g. the <c>infra_traefik</c> overlay subnet.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    ///     How many entries to walk back from the right of <c>X-Forwarded-For</c>. One, because exactly one
    ///     trusted hop (Traefik) appends to that header; a larger value would start reading caller-supplied
    ///     entries as if they were the client.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>True when <c>X-Forwarded-For</c> is to be honoured and a trust list actually exists.</summary>
    public bool UsesForwardedHeader =>
        ClientIpSource == ClientIpSource.ForwardedHeader &&
        (KnownProxies.Length > 0 || KnownNetworks.Length > 0);

    /// <summary>
    ///     True when <c>Connection.RemoteIpAddress</c> identifies the caller, so an IP-keyed partition is a
    ///     partition per client rather than one global bucket.
    /// </summary>
    public bool ClientIpIsTrustworthy =>
        ClientIpSource == ClientIpSource.ConnectionRemoteIp || UsesForwardedHeader;
}

public enum ClientIpSource
{
    /// <summary>
    ///     Not declared. The deployment topology is unknown, so no IP-keyed limit is applied at all —
    ///     shipping one anyway would risk a single shared bucket that any one caller can exhaust for
    ///     everybody. Per-caller partitions (e-mail, token subject) still apply.
    /// </summary>
    Unknown = 0,

    /// <summary>No proxy in front: the socket peer is the caller.</summary>
    ConnectionRemoteIp,

    /// <summary>Behind a trusted reverse proxy that appends <c>X-Forwarded-For</c>.</summary>
    ForwardedHeader
}
