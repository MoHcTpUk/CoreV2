namespace Users.Application.Interfaces;

/// <summary>
///     Out-of-band delivery channel for authentication verification codes.
///     The code is a shared secret that is exchanged for an access token, so it must reach the user
///     through this channel only — never through the response of the endpoint that created it.
/// </summary>
public interface IVerificationCodeSender
{
    Task SendAsync(string email, string verificationCode, CancellationToken cancellationToken = default);
}
