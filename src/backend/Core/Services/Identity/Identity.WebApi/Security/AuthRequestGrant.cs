namespace Identity.WebApi.Security;

/// <summary>
///     The custom grant that replaced the "generate a password, then log in with it" proxy.
///     <para>
///         A client still calls <c>/connect/token</c> with the auth request id and the verification code.
///         <see cref="Middleware.VerifyAuthToTokenMiddleware" /> exchanges those with the Users service for
///         a single-use ticket and rewrites the request as this grant, which the token controller redeems.
///         The ticket is the only thing that ever crosses the bus, and it opens exactly one session for
///         one user within a minute.
///     </para>
/// </summary>
public static class AuthRequestGrant
{
    /// <summary>Value of <c>grant_type</c> for the internal exchange.</summary>
    public const string GrantType = "auth_request";

    /// <summary>Form field carrying the single-use ticket.</summary>
    public const string TicketParameter = "ticket";
}
