using Identity.Application.Services;
using Identity.Contracts.Commands;
using Shared.Messaging.Interfaces;

namespace Identity.Application.Handlers;

public class IssueAuthTicketHandler(
    IAuthTicketService authTicketService
) : ICommandHandler<IssueAuthTicketCommand, IssueAuthTicketResult>
{
    public async Task<IssueAuthTicketResult> Handle(IssueAuthTicketCommand request,
        CancellationToken cancellationToken)
    {
        var issued = await authTicketService.IssueAsync(request.UserId, request.AuthRequestId,
            cancellationToken);

        return new IssueAuthTicketResult
        {
            Ticket = issued.Ticket,
            ExpiresAt = issued.ExpiresAt
        };
    }
}
