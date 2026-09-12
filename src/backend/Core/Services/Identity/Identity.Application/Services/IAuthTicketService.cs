using Identity.Domain.Aggregates;

namespace Identity.Application.Services;

public interface IAuthTicketService
{
    /// <summary>
    ///     Issues a single-use ticket for <paramref name="userId" /> and returns it in plain text. This is
    ///     the only moment the secret exists outside the caller's hands; only its hash is persisted.
    /// </summary>
    Task<IssuedAuthTicket> IssueAsync(Guid userId, Guid authRequestId, CancellationToken cancellationToken);

    /// <summary>
    ///     Spends the ticket and returns the user it was bound to, or <c>null</c> when it is unknown,
    ///     malformed, expired or already spent. Every failure looks the same to the caller on purpose.
    /// </summary>
    Task<ApplicationUser?> RedeemAsync(string ticket, CancellationToken cancellationToken);
}

/// <summary>A freshly minted ticket: the secret to hand out, and the moment it stops working.</summary>
public sealed record IssuedAuthTicket(string Ticket, DateTimeOffset ExpiresAt);
