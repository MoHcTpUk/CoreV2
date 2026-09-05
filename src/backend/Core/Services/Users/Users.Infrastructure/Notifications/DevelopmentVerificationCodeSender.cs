using Microsoft.Extensions.Logging;
using Users.Application.Interfaces;

namespace Users.Infrastructure.Notifications;

/// <summary>
///     Development delivery channel: writes the verification code to the service log instead of mailing it,
///     so a developer machine needs no SMTP server. Registered only for the Development and Local
///     environments — never for Stage or Production.
/// </summary>
public sealed class DevelopmentVerificationCodeSender(ILogger<DevelopmentVerificationCodeSender> logger)
    : IVerificationCodeSender
{
    public Task SendAsync(string email, string verificationCode, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "[DEVELOPMENT] Verification code for {Email} is {VerificationCode}. " +
            "This channel is never registered outside a developer environment.",
            email, verificationCode);

        return Task.CompletedTask;
    }
}
