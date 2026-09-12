using Shared.Domain.Interfaces;

namespace Identity.Domain.Aggregates;

/// <summary>
///     A one-shot permission to mint tokens for a single user, issued after the Users service has
///     verified an authentication request.
///     <para>
///         It exists so that no password has to travel between the services: the Users service asks
///         Identity for a ticket and hands it back to the token endpoint, which is the only place that
///         can redeem it. Only the SHA-256 of the secret is stored, so a dump of this table cannot be
///         replayed, and <see cref="RedeemedAt" /> makes the redemption single-use.
///     </para>
/// </summary>
public class AuthTicket : IAggregateRoot, IBaseModel
{
    public required Guid Id { get; set; }

    /// <summary>Identity user the ticket may mint tokens for. Nothing else can be logged in with it.</summary>
    public required Guid UserId { get; set; }

    /// <summary>The verified authentication request this ticket was issued for.</summary>
    public required Guid AuthRequestId { get; set; }

    /// <summary>Base64 of SHA-256 over the secret half of the ticket. The secret itself is never stored.</summary>
    public required string SecretHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC moment after which the ticket is refused. Measured in seconds, not minutes.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set the moment the ticket is spent; a second redemption finds it non-null and fails.</summary>
    public DateTimeOffset? RedeemedAt { get; set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
