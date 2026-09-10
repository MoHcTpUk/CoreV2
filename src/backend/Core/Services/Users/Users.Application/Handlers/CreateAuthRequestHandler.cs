using AutoMapper;
using Microsoft.Extensions.Options;
using Shared.Messaging.Interfaces;
using Users.Application.Interfaces;
using Users.Application.Options;
using Users.Application.Security;
using Users.Contracts.Commands.AuthRequest;
using Users.Contracts.Dtos;
using Users.Domain.Aggregates;

namespace Users.Application.Handlers;

public class CreateAuthRequestHandler(
    IAuthRequestRepository authRequestRepository,
    IVerificationCodeSender verificationCodeSender,
    IOptions<AuthRequestOptions> options,
    TimeProvider timeProvider,
    IMapper mapper)
    : ICommandHandler<CreateAuthRequestCommand, AuthRequestDto>
{
    public async Task<AuthRequestDto> Handle(CreateAuthRequestCommand requestCommand,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();

        var authRequest = new AuthRequest
        {
            Email = requestCommand.Email,
            ActivationCode = VerificationCodeGenerator.Generate(settings.VerificationCodeLength),
            Id = Guid.Empty,
            CreatedAt = now,
            ExpiresAt = now.Add(settings.Lifetime),
            AttemptCount = 0
        };

        var result =
            await authRequestRepository.CreateAsync([authRequest], cancellationToken);

        await authRequestRepository.SaveChangesAsync();

        var createdAuthRequest = result.First();

        // The code leaves the system only through the delivery channel; the DTO below carries no secret.
        await verificationCodeSender.SendAsync(createdAuthRequest.Email, createdAuthRequest.ActivationCode,
            cancellationToken);

        var dto = mapper.Map<AuthRequestDto>(createdAuthRequest);

        return dto;
    }
}
