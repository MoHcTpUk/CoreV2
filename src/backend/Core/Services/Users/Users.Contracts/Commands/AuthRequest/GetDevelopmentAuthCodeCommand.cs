using System.ComponentModel.DataAnnotations;
using Shared.Messaging.Interfaces;
using Users.Contracts.Dtos;

namespace Users.Contracts.Commands.AuthRequest;

/// <summary>
///     Development-only: reads back the verification code of an auth request so a developer can complete
///     the login flow without a mail server. The handler rejects the command in every environment other
///     than Development.
/// </summary>
public class GetDevelopmentAuthCodeCommand : ICommand<DevelopmentAuthCodeDto>
{
    [Required]
    public required Guid Id { get; set; }
}
