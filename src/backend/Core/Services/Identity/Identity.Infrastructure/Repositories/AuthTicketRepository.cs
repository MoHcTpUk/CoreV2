using Identity.Application.Interfaces;
using Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Repository;

namespace Identity.Infrastructure.Repositories;

public class AuthTicketRepository(IdentityDbContext context)
    : AbstractRepository<AuthTicket, IdentityDbContext>(context), IAuthTicketRepository
{
    private readonly IdentityDbContext _context = context;

    public async Task<AuthTicket?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.AuthTickets
            .AsNoTracking()
            .FirstOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);
    }

    public async Task<bool> TryRedeemAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One UPDATE ... WHERE RedeemedAt IS NULL: the database decides the winner, so two requests
        // arriving with the same ticket cannot both be handed a token.
        var affected = await _context.AuthTickets
            .Where(ticket => ticket.Id == id && ticket.RedeemedAt == null && ticket.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(ticket => ticket.RedeemedAt, now),
                cancellationToken);

        return affected == 1;
    }

    public async Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _context.AuthTickets
            .Where(ticket => ticket.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
