using Shared.Dtos;

namespace Users.Contracts.Dtos;

/// <summary>
///     Development-only payload that exposes the verification code of an existing auth request.
///     Produced exclusively by <c>GetDevelopmentAuthCodeCommand</c>, which refuses to run outside the
///     Development environment, and reachable only through the dev-only gateway endpoint.
/// </summary>
public record DevelopmentAuthCodeDto : Dto
{
    public required Guid Id { get; set; }

    public required string Email { get; set; }

    public required string VerificationCode { get; set; }
}
