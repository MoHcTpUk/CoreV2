using System.Security.Claims;
using Identity.Application.Services;
using Identity.Domain.Aggregates;
using Identity.WebApi.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Shared.Api.RateLimiting;

namespace Identity.WebApi.Controllers;

[ApiController]
[Route("connect")]
public class TokenController(
    IIdentityService identityService,
    IAuthTicketService authTicketService)
    : ControllerBase
{
    private const string OAuthErrorUri = "https://datatracker.ietf.org/doc/html/rfc6749#section-5.2";

    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.TokenPolicy)]
    [Produces("application/json")]

    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        try
        {
            var request = HttpContext.Features.Get<OpenIddictServerAspNetCoreFeature>()?.Transaction?.Request
                          ?? throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

            if (string.Equals(request.GrantType, AuthRequestGrant.GrantType, StringComparison.Ordinal))
                return await ExchangeAuthRequestAsync(request, cancellationToken);

            if (request.IsPasswordGrantType())
                return await ExchangePasswordAsync(request, cancellationToken);

            if (request.IsRefreshTokenGrantType())
                return await ExchangeRefreshTokenAsync(cancellationToken);

            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                        OpenIddictConstants.Errors.UnsupportedGrantType,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The specified grant type is not supported."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        catch (InvalidOperationException e)
        {
            return OAuthErrorResult(
                StatusCodes.Status400BadRequest,
                OpenIddictConstants.Errors.InvalidRequest,
                e.Message);
        }
        catch (Exception e)
        {
            return OAuthErrorResult(
                StatusCodes.Status500InternalServerError,
                OpenIddictConstants.Errors.ServerError,
                e.Message);
        }
    }

    /// <summary>
    ///     Redeems the single-use ticket minted for a verified authentication request. The ticket is the
    ///     whole credential: it names the user, it is spent here, and it stops working within a minute of
    ///     being issued, so there is no password to present and none to intercept.
    /// </summary>
    private async Task<IActionResult> ExchangeAuthRequestAsync(OpenIddictRequest request,
        CancellationToken cancellationToken)
    {
        var ticket = (string?)request.GetParameter(AuthRequestGrant.TicketParameter);

        var user = await authTicketService.RedeemAsync(ticket ?? string.Empty, cancellationToken);

        if (user is null)
        {
            // Unknown, expired, already spent or malformed all answer the same way: a caller guessing at
            // tickets must not learn which of those it hit.
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The authentication ticket is invalid or has expired."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return SignInUser(user, request);
    }

    private async Task<IActionResult> ExchangePasswordAsync(OpenIddictRequest request, CancellationToken cancellationToken)
    {
        var user = await identityService.ValidateUserAsync(
            request.Username ?? string.Empty,
            request.Password ?? string.Empty,
            cancellationToken);

        if (user is null)
        {
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid username/password."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return SignInUser(user, request);
    }

    private IActionResult SignInUser(ApplicationUser user, OpenIddictRequest request)
    {
        var principal = identityService.CreateClaimsPrincipal(user, request.GetScopes());
        principal.SetScopes(request.GetScopes());
        principal.SetResources("core-app", "gateway-webapi");

        foreach (var claim in principal.Claims) claim.SetDestinations(GetDestinations(claim, principal));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> ExchangeRefreshTokenAsync(CancellationToken cancellationToken)
    {
        var authenticateResult =
            await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var principal = authenticateResult.Principal;
        if (principal is null)
        {
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid refresh token."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var subject = principal.GetClaim(OpenIddictConstants.Claims.Subject);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid refresh token subject."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var user = await identityService.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token is no longer valid."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var scopes = principal.GetScopes();
        principal = identityService.CreateClaimsPrincipal(user, scopes);
        principal.SetScopes(scopes);
        principal.SetResources("core-app", "gateway-webapi");

        foreach (var claim in principal.Claims) claim.SetDestinations(GetDestinations(claim, principal));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private ObjectResult OAuthErrorResult(int statusCode, string error, string description)
    {
        var payload = new
        {
            error,
            error_description = description,
            error_uri = OAuthErrorUri
        };

        return new ObjectResult(payload)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/json" }
        };
    }

    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
    {
        switch (claim.Type)
        {
            case OpenIddictConstants.Claims.Subject:
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield return OpenIddictConstants.Destinations.IdentityToken;
                yield break;

            case OpenIddictConstants.Claims.Name:
            case OpenIddictConstants.Claims.PreferredUsername:
                yield return OpenIddictConstants.Destinations.AccessToken;
                if (principal.HasScope(OpenIddictConstants.Scopes.Profile))
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                yield break;

            case OpenIddictConstants.Claims.Email:
                yield return OpenIddictConstants.Destinations.AccessToken;
                if (principal.HasScope(OpenIddictConstants.Scopes.Email))
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                yield break;

            case OpenIddictConstants.Claims.Role:
                yield return OpenIddictConstants.Destinations.AccessToken;
                if (principal.HasScope(OpenIddictConstants.Scopes.Roles))
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                yield break;

            default:
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield break;
        }
    }
}