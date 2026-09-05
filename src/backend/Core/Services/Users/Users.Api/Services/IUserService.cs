using Shared.Api.Services;
using Users.Contracts.Commands.AuthRequest;
using Users.Contracts.Commands.User;
using Users.Contracts.Dtos;
using Users.Contracts.Queries;

namespace Users.Api.Services;

public interface IUserService : IService<CreateUserCommand, UpdateUserCommand, DeleteUserCommand, UserQuery, UserDto>
{
    Task<AuthRequestDto> CreateAuthRequest(CreateAuthRequestCommand requestCommand);

    Task<UserDto> VerifyAuthRequest(VerifyAuthRequestCommand requestCommand);

    /// <summary>
    ///     Development-only. Backed by a handler that refuses the command outside the Development
    ///     environment; the gateway exposes it through a dev-only endpoint.
    /// </summary>
    Task<DevelopmentAuthCodeDto> GetDevelopmentAuthCode(GetDevelopmentAuthCodeCommand requestCommand);
}