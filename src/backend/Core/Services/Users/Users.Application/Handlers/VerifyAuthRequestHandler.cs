using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using Identity.Contracts.Commands;
using Identity.Contracts.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Messaging.Interfaces;
using Users.Application.Interfaces;
using Users.Application.Options;
using Users.Application.Security;
using Users.Contracts.Commands.AuthRequest;
using Users.Contracts.Dtos;
using Users.Domain.Aggregates;

namespace Users.Application.Handlers;

public class VerifyAuthRequestHandler(
    IAuthRequestRepository authRequestRepository,
    IUserRepository userRepository,
    IIdentityService identityService,
    IOptions<AuthRequestOptions> options,
    TimeProvider timeProvider,
    IMapper mapper
)
    : ICommandHandler<VerifyAuthRequestCommand, UserDto>
{
    /// <summary>
    ///     Deliberately identical for "no such request", "expired" and "wrong code": telling those apart
    ///     would turn the endpoint into an oracle for guessing request identifiers.
    /// </summary>
    private const string InvalidCodeMessage = "The verification code is invalid or has expired.";

    /// <summary>
    ///     The lock-out message is distinct because reaching it requires having already spent the whole
    ///     attempt budget on this request, so it reveals nothing new — and the legitimate user has to be
    ///     told to ask for a fresh code.
    /// </summary>
    private const string LockedOutMessage =
        "Too many failed verification attempts. Request a new verification code.";

    public async Task<UserDto> Handle(VerifyAuthRequestCommand request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var authRequest = await authRequestRepository.AsQueryable()
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

        if (authRequest is null) throw new UnauthorizedAccessException(InvalidCodeMessage);

        // A request that ran out of attempts or out of time is worthless: drop it so the row cannot be
        // probed any further, then answer with the same message an unknown identifier would get.
        if (authRequest.IsOutOfAttempts(settings.MaxVerificationAttempts) ||
            authRequest.IsExpired(timeProvider.GetUtcNow()))
        {
            await ConsumeAsync(authRequest);
            throw new UnauthorizedAccessException(
                authRequest.IsOutOfAttempts(settings.MaxVerificationAttempts)
                    ? LockedOutMessage
                    : InvalidCodeMessage);
        }

        if (!VerificationCodeMatches(authRequest.ActivationCode, request.VerificationCode))
        {
            authRequest.RegisterFailedAttempt();

            if (authRequest.IsOutOfAttempts(settings.MaxVerificationAttempts))
            {
                await ConsumeAsync(authRequest);
                throw new UnauthorizedAccessException(LockedOutMessage);
            }

            authRequestRepository.UpdateAsync([authRequest]);
            await authRequestRepository.SaveChangesAsync();

            throw new UnauthorizedAccessException(InvalidCodeMessage);
        }

        // Single use: the request is consumed the moment its code matches, before any credential is
        // issued, so the same code can never be replayed — not even if a later step fails.
        await ConsumeAsync(authRequest);

        var existingUser = await userRepository.AsQueryable()
            .FirstOrDefaultAsync(item => item.Email == authRequest.Email, cancellationToken);

        if (existingUser is not null) return await ProcessExistUser(existingUser, authRequest.Email);

        return await ProcessNewUser(authRequest.Email, cancellationToken);
    }

    private async Task ConsumeAsync(AuthRequest authRequest)
    {
        authRequestRepository.DeleteAsync([authRequest]);
        await authRequestRepository.SaveChangesAsync();
    }

    private async Task<UserDto> ProcessNewUser(string email, CancellationToken cancellationToken)
    {
        var username = email.Split('@')[0].ToLowerInvariant();
        var temporaryPassword = TemporaryPasswordGenerator.Generate(options.Value.TemporaryPasswordLength);

        var createUserCommand = new CreateIdentityUserCommand
        {
            Email = email,
            UserName = username,
            Password = temporaryPassword
        };

        var identityUserId = await identityService.CreateUserAsync(createUserCommand);

        var newUser = new UserAggregate
        {
            Email = email,
            UserName = username,
            Id = identityUserId
        };

        await userRepository.CreateAsync([newUser], cancellationToken);
        await userRepository.SaveChangesAsync();

        var userDto = mapper.Map<UserDto>(newUser);
        userDto.Password = temporaryPassword;

        return userDto;
    }

    private async Task<UserDto> ProcessExistUser(UserAggregate existingUser, string email)
    {
        var temporaryPassword = TemporaryPasswordGenerator.Generate(options.Value.TemporaryPasswordLength);

        var updatePasswordCommand = new SetIdentityUserPasswordCommand
        {
            Email = email,
            Password = temporaryPassword
        };

        var updatePasswordResult = await identityService.SetUserPasswordAsync(updatePasswordCommand);

        if (!updatePasswordResult.Success)
            throw new Exception(updatePasswordResult.ErrorMessage ?? "Unable to update user password.");

        var userDto = mapper.Map<UserDto>(existingUser);
        userDto.Password = temporaryPassword;

        return userDto;
    }

    /// <summary>
    ///     Compares the two codes in time independent of their contents. Both sides are hashed first, so the
    ///     buffers handed to <see cref="CryptographicOperations.FixedTimeEquals" /> are always the same
    ///     fixed length and a wrong-length guess cannot be told apart from a wrong-value one.
    /// </summary>
    private static bool VerificationCodeMatches(string expected, string? provided)
    {
        Span<byte> expectedHash = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> providedHash = stackalloc byte[SHA256.HashSizeInBytes];

        SHA256.HashData(Encoding.UTF8.GetBytes(expected), expectedHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty), providedHash);

        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
