using Shared.Messaging.Interfaces;

namespace Identity.Contracts.Commands;

/// <summary>
///     Asks Identity for a one-shot, seconds-long permission to mint tokens for one user. Issued only
///     after the Users service has verified the authentication request named by
///     <see cref="AuthRequestId" />.
/// </summary>
public class IssueAuthTicketCommand : ICommand<IssueAuthTicketResult>
{
    /// <summary>Identity user the ticket is bound to. The ticket cannot log anyone else in.</summary>
    public required Guid UserId { get; set; }

    /// <summary>The verified authentication request the ticket is issued for.</summary>
    public required Guid AuthRequestId { get; set; }
}
