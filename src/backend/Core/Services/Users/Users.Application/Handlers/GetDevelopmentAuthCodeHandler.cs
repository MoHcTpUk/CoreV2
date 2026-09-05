using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.Interfaces;
using Users.Application.Interfaces;
using Users.Contracts.Commands.AuthRequest;
using Users.Contracts.Dtos;

namespace Users.Application.Handlers;

/// <summary>
///     Development-only companion to <see cref="CreateAuthRequestHandler" />: returns the verification code
///     of an existing auth request so the login flow can be completed without a mail server.
///     Two independent gates protect it — the gateway maps the endpoint only when the host environment is
///     Development, and this handler refuses the command in any other environment.
/// </summary>
public class GetDevelopmentAuthCodeHandler(
    IAuthRequestRepository authRequestRepository,
    IHostEnvironment environment)
    : ICommandHandler<GetDevelopmentAuthCodeCommand, DevelopmentAuthCodeDto>
{
    public async Task<DevelopmentAuthCodeDto> Handle(GetDevelopmentAuthCodeCommand request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            throw new UnauthorizedAccessException(
                "Reading verification codes is available in the Development environment only.");

        var authRequest = await authRequestRepository.AsQueryable()
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

        if (authRequest is null)
            throw new KeyNotFoundException("Auth request not found.");

        return new DevelopmentAuthCodeDto
        {
            Id = authRequest.Id,
            Email = authRequest.Email,
            VerificationCode = authRequest.ActivationCode
        };
    }
}
