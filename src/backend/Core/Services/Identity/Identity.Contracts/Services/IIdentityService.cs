using Identity.Contracts.Commands;

namespace Identity.Contracts.Services;

/// <summary>
///     Everything the Users service may ask Identity for over the bus. Deliberately narrow, and
///     deliberately free of credentials: an account is created without a password, and a session is opened
///     with a single-use ticket that Identity itself mints and later redeems.
/// </summary>
public interface IIdentityService
{
    Task<Guid> CreateUserAsync(CreateIdentityUserCommand command);

    Task<IssueAuthTicketResult> IssueAuthTicketAsync(IssueAuthTicketCommand command);
}
