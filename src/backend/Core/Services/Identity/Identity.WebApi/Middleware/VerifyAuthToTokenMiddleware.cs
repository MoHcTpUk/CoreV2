using System.Text;
using System.Text.Json;
using Shared.Messaging.Messaging;
using Identity.WebApi.Security;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using Users.Contracts.Commands.AuthRequest;
using Users.Api.Services;

namespace Identity.WebApi.Middleware;

/// <summary>
///     Translates the verification payload (<c>id</c> + <c>verificationCode</c>) into an
///     <see cref="AuthRequestGrant.GrantType" /> token request, within the same HTTP request.
///     <para>
///         The Users service answers the verification with a single-use ticket, never with a password:
///         nothing that could be replayed against the account, and nothing that survives past a minute,
///         ever travels over the message bus.
///     </para>
///     <para>
///         The ticket reaches the token controller only through <c>HttpContext.Items</c>
///         (<see cref="AuthRequestGrant.TicketItemKey" />). The rewritten form carries the grant type, scope
///         and client id and nothing secret, so the request OpenIddict extracts and logs holds no credential.
///     </para>
/// </summary>
public sealed class VerifyAuthToTokenMiddleware(RequestDelegate next)
{
    private const string OAuthErrorUri = "https://datatracker.ietf.org/doc/html/rfc6749#section-5.2";

    public async Task InvokeAsync(HttpContext context, IUserService userService, IConfiguration configuration)
    {
        var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !path.EndsWith("/connect/token", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        
        context.Request.EnableBuffering();

        var incomingForm = await context.Request.ReadFormAsync();
        
        if (context.Request.Body.CanSeek)
            context.Request.Body.Position = 0;

        context.Request.Form = incomingForm;
        context.Features.Set<IFormFeature>(new FormFeature(incomingForm));

        var hasVerificationCode = incomingForm.TryGetValue("verificationCode", out var verificationCodeValue);
        var verificationCode = hasVerificationCode ? verificationCodeValue.ToString() : string.Empty;

        var hasVerificationId = incomingForm.TryGetValue("id", out var verificationIdValue);

        if (hasVerificationCode ^ hasVerificationId)
        {
            await WriteOAuthErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                OpenIddictConstants.Errors.InvalidRequest,
                "Both 'id' and 'verificationCode' must be provided together.");
            return;
        }

        if (!hasVerificationCode || !hasVerificationId)
        {
            await next(context);
            return;
        }

        if (!Guid.TryParse(verificationIdValue.ToString(), out var verificationId))
        {
            await WriteOAuthErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                OpenIddictConstants.Errors.InvalidRequest,
                "Invalid id format.");
            return;
        }

        try
        {
            var command = new VerifyAuthRequestCommand
            {
                Id = verificationId,
                VerificationCode = verificationCode
            };

            var verified = await userService.VerifyAuthRequest(command);

            // Server-side per-request state, not a form field: OpenIddict would log the field verbatim, and
            // anything that can travel in the form can also be sent to /connect/token by an outside caller.
            context.Items[AuthRequestGrant.TicketItemKey] = verified.Ticket;
        }
        catch (RemoteProblemDetailsException ex)
        {
            await WriteOAuthErrorAsync(
                context,
                MapStatusCode(ex.Problem.Status),
                MapOAuthError(ex.Problem.Status),
                ex.Problem.Detail ?? ex.Message);
            return;
        }

        var scope = string.Join(' ', new[]
        {
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess
        });

        // No ticket here: the token controller takes it from HttpContext.Items (see above).
        var form = new Dictionary<string, string?>
        {
            ["grant_type"] = AuthRequestGrant.GrantType,
            ["scope"] = scope,
            ["client_id"] = configuration["OpenIddict:ClientId"]
        };

        var formString = await new FormUrlEncodedContent(form).ReadAsStringAsync();
        var formBytes = Encoding.UTF8.GetBytes(formString);
        
        // IMPORTANT: ReadFormAsync() caches the parsed form in IFormFeature.
        // Replacing only Request.Body won't affect model binding/OpenIddict unless we also update the cached form.
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.ContentLength = formBytes.Length;
        context.Request.Body = new MemoryStream(formBytes);

        var replacedForm = new FormCollection(
            form.ToDictionary(kvp => kvp.Key, kvp => new StringValues(kvp.Value)));

        context.Request.Form = replacedForm;
        context.Features.Set<IFormFeature>(new FormFeature(replacedForm));

        await next(context);
    }

    private static async Task WriteOAuthErrorAsync(
        HttpContext context,
        int statusCode,
        string error,
        string description)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            error,
            error_description = description,
            error_uri = OAuthErrorUri
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    private static string MapOAuthError(int? status)
    {
        return status switch
        {
            StatusCodes.Status400BadRequest => OpenIddictConstants.Errors.InvalidRequest,
            StatusCodes.Status401Unauthorized => OpenIddictConstants.Errors.InvalidClient,
            StatusCodes.Status403Forbidden => OpenIddictConstants.Errors.InvalidGrant,
            StatusCodes.Status404NotFound => OpenIddictConstants.Errors.InvalidRequest,
            _ => OpenIddictConstants.Errors.ServerError
        };
    }

    private static int MapStatusCode(int? status) =>
        status is > 0 ? status.Value : StatusCodes.Status400BadRequest;
}
