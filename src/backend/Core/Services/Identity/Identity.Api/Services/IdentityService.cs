using Identity.Contracts.Commands;
using Identity.Contracts.Services;
using Shared.Messaging.Messaging;

namespace Identity.Api.Services;

public class IdentityService(ICommandBus commandBus) : IIdentityService
{
    private const string IdentityServiceSubject = MessagingSubject.IdentityService;

    public async Task<Guid> CreateUserAsync(CreateIdentityUserCommand command)
    {
        var result = await commandBus.SendAsync<CreateIdentityUserCommand, Guid>(
            command, IdentityServiceSubject);

        return result;
    }

    public async Task<IssueAuthTicketResult> IssueAuthTicketAsync(IssueAuthTicketCommand command)
    {
        var result = await commandBus.SendAsync<IssueAuthTicketCommand, IssueAuthTicketResult>(
            command, IdentityServiceSubject);

        return result ?? throw new InvalidOperationException("No response from IdentityService");
    }
}
