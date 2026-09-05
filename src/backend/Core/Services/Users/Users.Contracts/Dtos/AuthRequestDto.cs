using Shared.Dtos;

namespace Users.Contracts.Dtos;

/// <summary>
///     Result of an authentication request. Intentionally carries no secret: the verification code is
///     delivered out-of-band (see <c>IVerificationCodeSender</c>) and must never leave through the API.
/// </summary>
public record AuthRequestDto : Dto
{
    public required string Email { get; set; }

    public required Guid Id { get; set; }
}
