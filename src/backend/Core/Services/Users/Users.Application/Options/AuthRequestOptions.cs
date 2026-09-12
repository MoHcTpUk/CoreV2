namespace Users.Application.Options;

/// <summary>
///     Limits that make a verification code impractical to guess, bound from the
///     <c>Authentication:AuthRequest</c> configuration section.
///     Every setter clamps its value, so a misconfigured appsettings file can only make the flow stricter
///     than the floor the security review requires — never weaker.
/// </summary>
public class AuthRequestOptions
{
    public const string SectionName = "Authentication:AuthRequest";

    private const int MinimumVerificationCodeLength = 6;
    private const int MinimumLifetimeMinutes = 5;
    private const int MaximumLifetimeMinutes = 10;

    private int _verificationCodeLength = MinimumVerificationCodeLength;
    private int _lifetimeMinutes = MaximumLifetimeMinutes;
    private int _maxVerificationAttempts = 5;

    /// <summary>Number of digits in the verification code. Never below six.</summary>
    public int VerificationCodeLength
    {
        get => _verificationCodeLength;
        set => _verificationCodeLength = Math.Max(MinimumVerificationCodeLength, value);
    }

    /// <summary>How long a code stays valid, in minutes. Clamped to the 5–10 minute band.</summary>
    public int LifetimeMinutes
    {
        get => _lifetimeMinutes;
        set => _lifetimeMinutes = Math.Clamp(value, MinimumLifetimeMinutes, MaximumLifetimeMinutes);
    }

    /// <summary>Failed attempts allowed before the request is invalidated. At least one.</summary>
    public int MaxVerificationAttempts
    {
        get => _maxVerificationAttempts;
        set => _maxVerificationAttempts = Math.Max(1, value);
    }

    public TimeSpan Lifetime => TimeSpan.FromMinutes(LifetimeMinutes);
}
