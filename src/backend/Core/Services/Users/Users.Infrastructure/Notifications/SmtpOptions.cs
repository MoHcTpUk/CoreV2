namespace Users.Infrastructure.Notifications;

/// <summary>
///     SMTP settings for verification-code delivery, bound from the <c>Notifications:Smtp</c> configuration
///     section. Credentials must come from environment variables or Docker secrets
///     (<c>Notifications__Smtp__UserName</c>, <c>Notifications__Smtp__Password</c>) — never from a committed
///     appsettings file.
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Notifications:Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromDisplayName { get; set; } = string.Empty;

    public string Subject { get; set; } = "Your verification code";

    /// <summary>Body of the message. <c>{code}</c> is replaced with the verification code.</summary>
    public string BodyTemplate { get; set; } = "Your verification code is {code}.";

    public int TimeoutMilliseconds { get; set; } = 10000;
}
