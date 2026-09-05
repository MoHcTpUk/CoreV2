using Shared.Dtos;

namespace Users.Contracts.Dtos;

public record UserDto : Dto
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    public required string Password { get; set; }

    public required string UserName { get; set; }
}
