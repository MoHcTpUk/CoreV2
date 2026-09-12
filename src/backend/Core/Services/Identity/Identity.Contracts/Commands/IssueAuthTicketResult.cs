namespace Identity.Contracts.Commands;

/// <summary>
///     The ticket to present at <c>/connect/token</c>. It is a bearer secret, but a narrow one: valid for
///     one user, one redemption and at most a minute, and it changes nothing about the account it opens.
/// </summary>
public class IssueAuthTicketResult
{
    public required string Ticket { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}
