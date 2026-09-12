using Shared.Messaging.Interfaces;

namespace Identity.Contracts.Commands;

/// <summary>
///     Creates the identity record for a user who has just proved control of an e-mail address.
///     Deliberately carries no credential: the account is created without a password and is logged into
///     with a single-use ticket (<see cref="IssueAuthTicketCommand" />), so nothing secret crosses the bus.
/// </summary>
public class CreateIdentityUserCommand : ICommand<Guid>
{
    public required string Email { get; set; }

    public required string UserName { get; set; }
}
