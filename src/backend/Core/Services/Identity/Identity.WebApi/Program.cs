using Identity.WebApi;
using Identity.WebApi.Middleware;
using Shared.Api.Filters;
using Shared.Api.RateLimiting;
using Environments = Shared.Environments;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OpenApiDocumentTransformer>();
});
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddControllers(options => { options.Filters.Add<RemoteProblemDetailsExceptionFilter>(); });
builder.Services.AddIdentityWebApi(builder.Configuration, builder.Environment);
builder.Services.AddAuthRateLimiting(builder.Configuration);

builder.Services.AddHostedService<AppInitializer>();

if (builder.Environment.IsDevelopment() || builder.Environment.EnvironmentName == Environments.Local)
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("NoLocalCORS", policy =>
        {
            policy
                .SetIsOriginAllowed(origin =>
                {
                    try
                    {
                        var uri = new Uri(origin);
                        var isLocalhost = uri.Host == "localhost" || uri.Host == "127.0.0.1";

                        var isCoreappLocal = uri.Host == "coreapp.local" || uri.Host.EndsWith(".coreapp.local");

                        return isLocalhost || isCoreappLocal;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

var app = builder.Build();

// First in the pipeline, for the same reason as in the gateway: the limiter partitions on the client
// address and must not see Traefik's.
app.UseTrustedProxy();

if (app.Environment.IsProduction()) app.UseHttpsRedirection();

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == Environments.Local)
    app.UseCors("NoLocalCORS");

// Map OpenAPI before authentication middleware to ensure it's accessible
if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == Environments.Local)
    app.MapOpenApi().AllowAnonymous();

// The token subject has to be read from the form before the limiter runs, because a partition key is
// chosen synchronously and cannot read the body itself.
app.UseTokenSubjectPartitionKey("/connect/token");

// Before the verification middleware: a guess must be counted against the limit even when the request
// never reaches the token controller.
app.UseRateLimiter();

// Allows issuing tokens from verification flows by exchanging the verification payload for a
// single-use ticket and translating it into the internal 'auth_request' grant, within the same
// HTTP request. No password is involved at any point.
app.UseMiddleware<VerifyAuthToTokenMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();