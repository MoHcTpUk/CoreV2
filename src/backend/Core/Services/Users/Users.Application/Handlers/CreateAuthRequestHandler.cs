using System.Text;
using AutoMapper;
using Shared.Messaging.Interfaces;
using Users.Application.Interfaces;
using Users.Contracts.Commands.AuthRequest;
using Users.Contracts.Dtos;
using Users.Domain.Aggregates;

namespace Users.Application.Handlers;

public class CreateAuthRequestHandler(
    IAuthRequestRepository authRequestRepository,
    IVerificationCodeSender verificationCodeSender,
    IMapper mapper)
    : ICommandHandler<CreateAuthRequestCommand, AuthRequestDto>
{
    private const int VerificationCodeLength = 4;

    public async Task<AuthRequestDto> Handle(CreateAuthRequestCommand requestCommand,
        CancellationToken cancellationToken)
    {
        var activationCodeBuilder = new StringBuilder();
        var random = new Random();
        for (var i = 0; i < VerificationCodeLength; i++) activationCodeBuilder.Append(random.Next(10));
        var activationCode = activationCodeBuilder.ToString();

        var authRequest = new AuthRequest
        {
            Email = requestCommand.Email,
            ActivationCode = activationCode,
            Id = Guid.Empty
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
