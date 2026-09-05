using Shared.Messaging.Messaging;
using Users.Api.Services;
using Users.Contracts.Commands.AuthRequest;
using Users.Contracts.Dtos;

namespace Gateway.Api.Endpoints;

/// <summary>
///     Endpoints that exist only so a developer can complete the login flow on a machine without a mail
///     server. They expose the verification code, so they are mapped only when the host environment is
///     Development and are additionally re-checked by the handler behind the bus. On Stage and Production
///     the routes are never registered and the paths return 404.
/// </summary>
public static class DevelopmentAuthEndpoints
{
    public static WebApplication MapDevelopmentAuthEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;

        app.MapGet("/dev/auth-requests/{id:guid}/verification-code",
                async (Guid id, IUserService userService) =>
                {
                    try
                    {
                        var result = await userService.GetDevelopmentAuthCode(
                            new GetDevelopmentAuthCodeCommand { Id = id });

                        return Results.Ok(result);
                    }
                    catch (RemoteProblemDetailsException e)
                    {
                        // Minimal API endpoints do not run the MVC RemoteProblemDetailsExceptionFilter.
                        return Results.Json(
                            e.Problem,
                            statusCode: e.Problem.Status ?? StatusCodes.Status500InternalServerError,
                            contentType: "application/problem+json");
                    }
                })
            .AllowAnonymous()
            .WithName("GetDevelopmentVerificationCode")
            .WithSummary("Development only. Returns the verification code of an auth request.")
            .Produces<DevelopmentAuthCodeDto>();

        return app;
    }
}
