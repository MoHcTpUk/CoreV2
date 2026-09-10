namespace Shared.Api.RateLimiting;

/// <summary>
///     Fixed-window limits for the anonymous authentication endpoints, bound from the <c>RateLimiting</c>
///     configuration section. Everything is configurable per deployment; the defaults are the values the
///     security review asks for when nothing is configured.
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Creating a verification request, partitioned by client IP.</summary>
    public RateLimitWindowOptions AuthRequest { get; set; } = new() { PermitLimit = 10, WindowSeconds = 300 };

    /// <summary>Creating a verification request, partitioned by the target e-mail address.</summary>
    public RateLimitWindowOptions AuthRequestPerEmail { get; set; } = new() { PermitLimit = 3, WindowSeconds = 300 };

    /// <summary>Exchanging credentials at the token endpoint, partitioned by client IP.</summary>
    public RateLimitWindowOptions Token { get; set; } = new() { PermitLimit = 20, WindowSeconds = 300 };

    /// <summary>
    ///     Exchanging credentials at the token endpoint, partitioned by what the exchange is aimed at: the
    ///     auth request id being verified, or the username being logged into. This is the arm that caps
    ///     guesses at one verification code however many addresses the caller comes from.
    /// </summary>
    public RateLimitWindowOptions TokenPerSubject { get; set; } = new() { PermitLimit = 10, WindowSeconds = 300 };
}

/// <summary>One fixed window: at most <see cref="PermitLimit" /> requests per <see cref="WindowSeconds" />.</summary>
public class RateLimitWindowOptions
{
    private int _permitLimit = 10;
    private int _windowSeconds = 300;
    private int _queueLimit;

    /// <summary>Set to <c>false</c> to turn this particular limit off (useful for local debugging).</summary>
    public bool Enabled { get; set; } = true;

    public int PermitLimit
    {
        get => _permitLimit;
        set => _permitLimit = Math.Max(1, value);
    }

    public int WindowSeconds
    {
        get => _windowSeconds;
        set => _windowSeconds = Math.Max(1, value);
    }

    /// <summary>Requests parked instead of rejected. Zero — reject immediately — is the right default here.</summary>
    public int QueueLimit
    {
        get => _queueLimit;
        set => _queueLimit = Math.Max(0, value);
    }

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
