using Shared.Domain.Interfaces;

namespace Identity.Domain.Aggregates;

public class ApplicationUser : IAggregateRoot, IBaseModel
{
    public required string Email { get; set; }
    public required string UserName { get; set; }

    /// <summary>
    ///     Null for every account opened through e-mail verification: those users have no password to
    ///     hash. Only an account that was deliberately given one can be logged into with the password
    ///     grant.
    /// </summary>
    public string? PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public required Guid Id { get; set; }
}
