using Shared.Domain.Interfaces;

namespace Users.Domain.Aggregates;

/// <summary>
///     A pending e-mail verification. The activation code is a short shared secret, so the aggregate also
///     carries the two limits that make guessing it impractical: an absolute expiry and a counter of failed
///     verification attempts after which the request is thrown away.
/// </summary>
public class AuthRequest : IAggregateRoot
{
    public required string Email { get; set; }

    public required string ActivationCode { get; set; }

    public required Guid Id { get; set; }

    /// <summary>UTC moment the request was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC moment from which the activation code is no longer accepted.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Number of verification attempts made with a wrong code.</summary>
    public int AttemptCount { get; set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsOutOfAttempts(int maxAttempts) => AttemptCount >= maxAttempts;

    public void RegisterFailedAttempt() => AttemptCount++;
}
