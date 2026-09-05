using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Users.Application.Interfaces;

namespace Users.Infrastructure.Notifications;

/// <summary>
///     Real delivery channel: mails the verification code to the address that requested it.
///     Registered outside the Development environment.
/// </summary>
public sealed class SmtpVerificationCodeSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpVerificationCodeSender> logger)
    : IVerificationCodeSender
{
    public async Task SendAsync(string email, string verificationCode,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException(
                $"Verification code delivery is not configured. Set '{SmtpOptions.SectionName}:Host' and " +
                $"'{SmtpOptions.SectionName}:FromAddress'.");

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            Timeout = settings.TimeoutMilliseconds,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (string.IsNullOrWhiteSpace(settings.UserName))
        {
            client.UseDefaultCredentials = true;
        }
        else
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        }

        var from = string.IsNullOrWhiteSpace(settings.FromDisplayName)
            ? new MailAddress(settings.FromAddress)
            : new MailAddress(settings.FromAddress, settings.FromDisplayName);

        using var message = new MailMessage
        {
            From = from,
            Subject = settings.Subject,
            Body = settings.BodyTemplate.Replace("{code}", verificationCode, StringComparison.Ordinal),
            IsBodyHtml = false
        };
        message.To.Add(email);

        await client.SendMailAsync(message, cancellationToken);

        // Deliberately never logs the code itself.
        logger.LogInformation("Verification code delivered to {Email} over SMTP.", email);
    }
}
