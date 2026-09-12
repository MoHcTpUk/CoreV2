using Shared.Dtos;

namespace Users.Contracts.Dtos;

/// <summary>
///     Read model of a user. Carries no credential of any kind: the login flow never produces one, and a
///     model that is returned by <c>GetUser</c> has no business holding a secret.
/// </summary>
public record UserDto : Dto
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    public required string UserName { get; set; }
}
