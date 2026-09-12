using System.Security.Claims;
using Identity.Domain.Aggregates;

namespace Identity.Application.Services;

public interface IIdentityService
{
    /// <summary>
    ///     Creates a user with no password at all. Accounts are opened by proving control of an e-mail
    ///     address, and logged into with a single-use ticket, so there is no credential to invent here.
    /// </summary>
    Task<Guid> CreateUserAsync(string email, string userName, CancellationToken cancellationToken);

    /// <summary>
    ///     Sets a password on an existing account. Not exposed over the message bus and not reachable from
    ///     the login flow: the only way in is a deliberate in-process caller, so no command can reset
    ///     somebody's credentials by naming their e-mail address.
    /// </summary>
    Task SetUserPasswordAsync(string email, string password, CancellationToken cancellationToken);
    Task<ApplicationUser?> ValidateUserAsync(string email, string password, CancellationToken cancellationToken);
    Task<ApplicationUser?> GetUserByEmailAsync(string email, CancellationToken cancellationToken);
    Task<ApplicationUser?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken);
    ClaimsPrincipal CreateClaimsPrincipal(ApplicationUser user, IEnumerable<string> scopes);
}
