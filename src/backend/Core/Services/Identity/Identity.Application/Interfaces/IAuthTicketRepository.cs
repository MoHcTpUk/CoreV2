using Identity.Domain.Aggregates;
using Shared.Domain.Interfaces;

namespace Identity.Application.Interfaces;

public interface IAuthTicketRepository : IRepository<AuthTicket>
{
    Task<AuthTicket?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Marks the ticket spent, but only if it is still unspent and not yet expired. The check and the
    ///     write are a single statement, so two redemptions racing over the same ticket cannot both win —
    ///     exactly one of them gets <c>true</c>.
    /// </summary>
    Task<bool> TryRedeemAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Removes tickets that can no longer be redeemed. Housekeeping, never a security control.</summary>
    Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
