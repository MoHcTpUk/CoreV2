using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Shared.Api.RateLimiting;

/// <summary>
///     Rate limiting for the endpoints an unauthenticated caller can reach: creating a verification request
///     and exchanging credentials for a token. Without it a six-digit code is still only a million guesses
///     away, so these limits are what make the attempt counter on the request meaningful.
///     Every endpoint gets a partition keyed on something the caller cannot rotate for free — the target
///     e-mail, the auth request being guessed at, or the account being logged into — and, only where the
///     deployment can vouch for it, a second one keyed on the client address.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>Policy name for the endpoint that creates a verification request.</summary>
    public const string AuthRequestPolicy = "auth-request";

    /// <summary>Policy name for the OAuth token endpoint.</summary>
    public const string TokenPolicy = "token";

    /// <summary>
    ///     <see cref="HttpContext.Items" /> key holding the e-mail address a request is aimed at. Populated
    ///     by <see cref="UseAuthRequestEmailPartitionKey" /> before the rate limiter runs.
    /// </summary>
    public const string EmailPartitionKeyItem = "RateLimiting:Email";

    /// <summary>
    ///     <see cref="HttpContext.Items" /> key holding the subject of a token exchange — the auth request
    ///     id for a verification exchange, otherwise the username. Populated by
    ///     <see cref="UseTokenSubjectPartitionKey" />.
    /// </summary>
    public const string SubjectPartitionKeyItem = "RateLimiting:Subject";

    /// <summary>Hard cap on how much of a request body is buffered to find a partition key.</summary>
    private const int MaximumInspectedBodyBytes = 4096;

    /// <summary>Cap on a partition key built from caller-supplied text.</summary>
    private const int MaximumPartitionKeyLength = 256;

    /// <summary>
    ///     Limits are read once here rather than through <see cref="Microsoft.Extensions.Options.IOptions{T}" />:
    ///     a partition callback is synchronous and hot, and the values are captured in the closures below,
    ///     so a configuration reload does not affect a running process. Restart to change them.
    /// </summary>
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTrustedProxy(configuration);

        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
                      ?? new RateLimitingOptions();
        var proxy = configuration.GetSection(TrustedProxyOptions.SectionName).Get<TrustedProxyOptions>()
                    ?? new TrustedProxyOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(AuthRequestPolicy,
                context => ByClientIp(context, options.AuthRequest, proxy, AuthRequestPolicy));

            limiter.AddPolicy(TokenPolicy,
                context => ByClientIp(context, options.Token, proxy, TokenPolicy));

            // Runs in addition to the endpoint policy: this is the dimension the caller cannot rotate by
            // changing address, and the only one that still applies when the client IP is not trustworthy.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => ByCallerIdentity(context, options));

            limiter.OnRejected = WriteTooManyRequestsAsync;
        });

        return services;
    }

    /// <summary>
    ///     Extracts the target e-mail from the JSON body of an auth-request call and stores it for the
    ///     e-mail partition. Must be registered before <c>UseRateLimiter</c>; a rate-limit partition key is
    ///     chosen synchronously and so cannot read the body itself.
    ///     The body is read here rather than trusted from <c>Content-Length</c>, and anything longer than
    ///     <see cref="MaximumInspectedBodyBytes" /> is rejected outright: if a request on this path could
    ///     reach the endpoint without yielding a key, omitting a header would be enough to opt out of the
    ///     limit.
    /// </summary>
    public static IApplicationBuilder UseAuthRequestEmailPartitionKey(this IApplicationBuilder app,
        PathString path)
    {
        return app.Use(async (context, next) =>
        {
            var request = context.Request;

            if (!HttpMethods.IsPost(request.Method) ||
                !IsExactPath(request.Path, path) ||
                request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            {
                await next();
                return;
            }

            if (!await TryCaptureEmailAsync(context)) return;

            await next();
        });
    }

    /// <summary>
    ///     Captures what a token exchange is aimed at: the auth request id when the verification flow is
    ///     being driven, otherwise the username. <c>/connect/token</c> is where a verification code is
    ///     actually guessed, so it needs a partition that does not depend on the caller's address.
    /// </summary>
    public static IApplicationBuilder UseTokenSubjectPartitionKey(this IApplicationBuilder app,
        PathString path)
    {
        return app.Use(async (context, next) =>
        {
            var request = context.Request;

            if (HttpMethods.IsPost(request.Method) && IsExactPath(request.Path, path) &&
                request.HasFormContentType)
                try
                {
                    // Reading the form here is the same read OpenIddict performs a moment later; the
                    // parsed result is cached on the request, so this adds no second pass over the body.
                    request.EnableBuffering();

                    var form = await request.ReadFormAsync(context.RequestAborted);
                    var subject = ReadTokenSubject(form);

                    if (subject is not null) context.Items[SubjectPartitionKeyItem] = subject;

                    if (request.Body.CanSeek) request.Body.Position = 0;
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException)
                {
                    // Unparseable form: the token endpoint will reject it, and the address partition
                    // (where trustworthy) still applies.
                }

            await next();
        });
    }

    /// <summary>
    ///     Buffers at most <see cref="MaximumInspectedBodyBytes" /> of the body, replaces the request body
    ///     with the buffer so model binding downstream is unaffected, and records the e-mail. Returns false
    ///     when the request has been rejected and the pipeline must stop.
    /// </summary>
    private static async Task<bool> TryCaptureEmailAsync(HttpContext context)
    {
        var request = context.Request;

        if (request.ContentLength > MaximumInspectedBodyBytes)
        {
            await WritePayloadTooLargeAsync(context);
            return false;
        }

        // Read one byte past the cap so an over-long body is detected rather than silently truncated.
        // Content-Length is not consulted: Kestrel leaves it null for a chunked or unlengthed HTTP/2
        // body, which would otherwise skip key capture while the endpoint still served the request.
        var buffered = new MemoryStream();
        var length = await CopyBoundedAsync(request.Body, buffered, MaximumInspectedBodyBytes + 1,
            context.RequestAborted);

        if (length > MaximumInspectedBodyBytes)
        {
            await WritePayloadTooLargeAsync(context);
            return false;
        }

        buffered.Position = 0;
        request.Body = buffered;
        request.ContentLength = length;

        try
        {
            using var document = JsonDocument.Parse(buffered);
            var email = ReadEmail(document.RootElement);

            if (email is not null) context.Items[EmailPartitionKeyItem] = email;
        }
        catch (JsonException)
        {
            // Malformed body: model binding rejects it, so no code is ever sent and nothing is bypassed.
        }

        buffered.Position = 0;

        return true;
    }

    private static async Task<long> CopyBoundedAsync(Stream source, Stream destination, int limit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        long total = 0;

        while (total < limit)
        {
            var wanted = (int)Math.Min(buffer.Length, limit - total);
            var read = await source.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);

            if (read == 0) break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }

        return total;
    }

    private static string? ReadEmail(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "email", StringComparison.OrdinalIgnoreCase)) continue;

            if (property.Value.ValueKind != JsonValueKind.String) return null;

            var email = property.Value.GetString();

            return string.IsNullOrWhiteSpace(email) ? null : Normalize(email);
        }

        return null;
    }

    private static string? ReadTokenSubject(IFormCollection form)
    {
        // The verification exchange names the auth request it is guessing at...
        if (form.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id))
            return $"id:{Normalize(id.ToString())}";

        // ...and an ordinary password grant names the account being logged into.
        if (form.TryGetValue("username", out var username) && !string.IsNullOrWhiteSpace(username))
            return $"user:{Normalize(username.ToString())}";

        // A refresh-token grant names neither; it presents a bearer token that is not guessable digit by
        // digit, so the address partition is left to cover it.
        return null;
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();

        return normalized.Length <= MaximumPartitionKeyLength
            ? normalized
            : normalized[..MaximumPartitionKeyLength];
    }

    private static RateLimitPartition<string> ByClientIp(HttpContext context, RateLimitWindowOptions window,
        TrustedProxyOptions proxy, string policy)
    {
        if (!window.Enabled) return RateLimitPartition.GetNoLimiter($"{policy}:disabled");

        // Only partition on the address when the deployment has declared where that address comes from.
        // Behind an unacknowledged proxy every caller shares the proxy's address, and an IP-keyed window
        // would be one bucket for the whole internet that any single caller could exhaust for everybody —
        // an outage dressed up as a per-client limit. Better no limit here than that.
        if (!proxy.ClientIpIsTrustworthy) return RateLimitPartition.GetNoLimiter($"{policy}:untrusted-ip");

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return FixedWindow($"{policy}:ip:{clientIp}", window);
    }

    private static RateLimitPartition<string> ByCallerIdentity(HttpContext context, RateLimitingOptions options)
    {
        if (context.Items[EmailPartitionKeyItem] is string email && email.Length > 0)
            return options.AuthRequestPerEmail.Enabled
                ? FixedWindow($"{AuthRequestPolicy}:email:{email}", options.AuthRequestPerEmail)
                : RateLimitPartition.GetNoLimiter("global:email-disabled");

        if (context.Items[SubjectPartitionKeyItem] is string subject && subject.Length > 0)
            return options.TokenPerSubject.Enabled
                ? FixedWindow($"{TokenPolicy}:subject:{subject}", options.TokenPerSubject)
                : RateLimitPartition.GetNoLimiter("global:subject-disabled");

        return RateLimitPartition.GetNoLimiter("global:none");
    }

    private static bool IsExactPath(PathString requestPath, PathString target)
    {
        var value = requestPath.Value;

        if (string.IsNullOrEmpty(value)) return false;

        // Routing accepts a single trailing slash on the endpoint, so the guard has to as well.
        if (value.Length > 1 && value[^1] == '/') value = value[..^1];

        return string.Equals(value, target.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static RateLimitPartition<string> FixedWindow(string key, RateLimitWindowOptions window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = window.PermitLimit,
            Window = window.Window,
            QueueLimit = window.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });

    private static async ValueTask WriteTooManyRequestsAsync(OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(NumberFormatInfo.InvariantInfo);

        await WriteProblemAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
            "Too Many Requests", "Too many requests. Try again later.", cancellationToken);
    }

    private static Task WritePayloadTooLargeAsync(HttpContext context) =>
        WriteProblemAsync(context, StatusCodes.Status413PayloadTooLarge, "Payload Too Large",
            $"The request body must be at most {MaximumInspectedBodyBytes} bytes.", context.RequestAborted);

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail,
        CancellationToken cancellationToken)
    {
        var response = context.Response;

        response.StatusCode = status;
        response.ContentType = "application/problem+json";

        await response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail
        }), cancellationToken);
    }
}
