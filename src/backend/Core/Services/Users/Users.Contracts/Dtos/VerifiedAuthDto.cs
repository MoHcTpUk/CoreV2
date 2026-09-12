using Shared.Dtos;

namespace Users.Contracts.Dtos;

/// <summary>
///     Result of a successful code verification. The only thing it carries is a single-use ticket issued
///     by Identity: it is bound to one user, dies within a minute, and cannot be replayed. No password is
///     produced by the login flow, so none crosses the bus.
/// </summary>
public record VerifiedAuthDto : Dto
{
    public required string Ticket { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}
