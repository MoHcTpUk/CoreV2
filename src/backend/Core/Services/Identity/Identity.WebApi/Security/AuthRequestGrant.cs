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
///     <para>
///         Inside Identity.WebApi the ticket never becomes part of the OAuth request. The middleware hands it
///         to the controller through <see cref="TicketItemKey" /> in <c>HttpContext.Items</c>: OpenIddict logs
///         every extracted token request and redacts only the parameters it knows to be secret, and a form
///         field could just as well be sent by an outside caller. The grant is therefore internal-only: a
///         request that names it without having passed the middleware carries no ticket and is refused.
///     </para>
/// </summary>
public static class AuthRequestGrant
{
    /// <summary>Value of <c>grant_type</c> for the internal exchange.</summary>
    public const string GrantType = "auth_request";

    /// <summary>
    ///     <c>HttpContext.Items</c> key under which the middleware passes the verified ticket to the token
    ///     controller. Server-side per-request state only: nothing a caller sends can set it.
    /// </summary>
    public const string TicketItemKey = "AuthRequestGrant:Ticket";
}
