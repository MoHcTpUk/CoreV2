using System.Security.Cryptography;
using System.Text;
using Identity.Application.Interfaces;
using Identity.Application.Options;
using Identity.Domain.Aggregates;
using Microsoft.Extensions.Options;

namespace Identity.Application.Services;

/// <summary>
///     Issues and redeems the short-lived login tickets that replaced the temporary-password proxy in the
///     verification flow.
///     <para>
///         A ticket is <c>{id}.{secret}</c>: the identifier is only a lookup key, the secret is 32 bytes
///         from the cryptographic RNG. The database keeps the SHA-256 of the secret, so possession of the
///         row is not possession of the ticket, and the comparison below runs in constant time.
///     </para>
/// </summary>
public class AuthTicketService(
    IAuthTicketRepository authTicketRepository,
    IApplicationUserRepository userRepository,
    IOptions<AuthTicketOptions> options,
    TimeProvider timeProvider) : IAuthTicketService
{
    private const int SecretSizeInBytes = 32;
    private const char Separator = '.';

    public async Task<IssuedAuthTicket> IssueAsync(Guid userId, Guid authRequestId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetAsync(userId, cancellationToken);

        if (user is null)
            throw new InvalidOperationException($"User with id {userId} does not exist");

        var now = timeProvider.GetUtcNow();

        // Opportunistic housekeeping: an unredeemed ticket is worthless a minute after it was made, and
        // leaving the rows around only grows a table nobody reads.
        await authTicketRepository.DeleteExpiredAsync(now, cancellationToken);

        var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretSizeInBytes));
        var expiresAt = now.Add(options.Value.Lifetime);

        var ticket = new AuthTicket
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthRequestId = authRequestId,
            SecretHash = HashSecret(secret),
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        await authTicketRepository.CreateAsync([ticket], cancellationToken);
        await authTicketRepository.SaveChangesAsync();

        return new IssuedAuthTicket($"{ticket.Id:N}{Separator}{secret}", expiresAt);
    }

    public async Task<ApplicationUser?> RedeemAsync(string ticket, CancellationToken cancellationToken)
    {
        if (!TrySplit(ticket, out var id, out var secret)) return null;

        var stored = await authTicketRepository.GetByIdAsync(id, cancellationToken);

        if (stored is null) return null;

        // The hash is checked before the expiry and the single-use flag so that a caller guessing at
        // identifiers learns nothing from how long the answer took or which rejection it was.
        if (!SecretMatches(stored.SecretHash, secret)) return null;

        var now = timeProvider.GetUtcNow();

        if (stored.RedeemedAt is not null || stored.IsExpired(now)) return null;

        // The row is spent here, not above: whoever wins this update is the only caller that gets a token.
        if (!await authTicketRepository.TryRedeemAsync(stored.Id, now, cancellationToken)) return null;

        return await userRepository.GetAsync(stored.UserId, cancellationToken);
    }

    private static bool TrySplit(string? ticket, out Guid id, out string secret)
    {
        id = Guid.Empty;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(ticket)) return false;

        var separatorIndex = ticket.IndexOf(Separator);

        if (separatorIndex <= 0 || separatorIndex == ticket.Length - 1) return false;

        if (!Guid.TryParseExact(ticket[..separatorIndex], "N", out id)) return false;

        secret = ticket[(separatorIndex + 1)..];

        return true;
    }

    private static string HashSecret(string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>
    ///     Compares the presented secret with the stored digest in time independent of their contents:
    ///     both sides are fixed-length SHA-256 buffers, so a wrong length cannot be told from a wrong value.
    /// </summary>
    private static bool SecretMatches(string storedHash, string providedSecret)
    {
        Span<byte> expected = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> provided = stackalloc byte[SHA256.HashSizeInBytes];

        if (!Convert.TryFromBase64String(storedHash, expected, out var written) ||
            written != SHA256.HashSizeInBytes)
            return false;

        SHA256.HashData(Encoding.UTF8.GetBytes(providedSecret), provided);

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
