namespace Identity.Application.Options;

/// <summary>
///     Lifetime of the single-use login ticket, bound from the <c>Authentication:AuthTicket</c>
///     configuration section. The setter clamps the value: a misconfigured appsettings file can shorten
///     the window but never stretch it past the minute the security review allows.
/// </summary>
public class AuthTicketOptions
{
    public const string SectionName = "Authentication:AuthTicket";

    private const int MinimumLifetimeSeconds = 5;
    private const int MaximumLifetimeSeconds = 60;

    private int _lifetimeSeconds = MaximumLifetimeSeconds;

    /// <summary>How long a freshly issued ticket may be redeemed, in seconds. Clamped to 5–60.</summary>
    public int LifetimeSeconds
    {
        get => _lifetimeSeconds;
        set => _lifetimeSeconds = Math.Clamp(value, MinimumLifetimeSeconds, MaximumLifetimeSeconds);
    }

    public TimeSpan Lifetime => TimeSpan.FromSeconds(LifetimeSeconds);
}
