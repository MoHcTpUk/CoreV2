using Gateway.Api;
using Gateway.Api.Endpoints;
using Sample.Api;
using Users.Api;
using Environments = Shared.Environments;
using Shared.Api.Filters;
using Shared.Api.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OpenApiDocumentTransformer>();
});
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<RemoteProblemDetailsExceptionFilter>();
    options.Filters.Add<ModelStateProblemDetailsFilter>();
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

builder.Services.ConfigureHttpClientDefaults(httpBuilder =>
{
    // OpenIddict.Validation.SystemNetHttp expects HttpClientHandler as primary handler.
    httpBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler());
});

var openIddictIssuer = builder.Configuration["OpenIddict:Issuer"] ??
                       (builder.Environment.IsDevelopment() || builder.Environment.EnvironmentName == Environments.Local
                           ? "http://localhost:5126/"
                           : "http://identity-webapi:8080/");
var openIddictAudience = builder.Configuration["OpenIddict:Audience"] ?? "core-app";

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});

builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(new Uri(openIddictIssuer));
        options.AddAudiences(openIddictAudience);
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

builder.Services.AddAuthorization();

builder.Services.AddAuthRateLimiting(builder.Configuration);

builder.Services.AddUsersApi(builder.Configuration);
builder.Services.AddSampleApi(builder.Configuration);
builder.Services.AddIdentityApi(builder.Configuration);

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

// First in the pipeline: everything downstream that reads the client address — the rate limiter above
// all — has to see the value X-Forwarded-For carries rather than Traefik's own.
app.UseTrustedProxy();

if (app.Environment.IsProduction()) app.UseHttpsRedirection();

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == Environments.Local)
{
    app.UseCors("NoLocalCORS");
}

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == Environments.Local)
{
    app.MapOpenApi().AllowAnonymous();
}

// The e-mail partition key has to be read from the request body, so it is captured before the limiter runs.
app.UseAuthRequestEmailPartitionKey("/Users/CreateAuthRequest");
app.UseRateLimiter();

app.MapGet("/test", () => "Hello from Gateway!").AllowAnonymous();

// Registered only when the environment is Development — see DevelopmentAuthEndpoints.
app.MapDevelopmentAuthEndpoints();

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/connect", StringComparison.OrdinalIgnoreCase),
    branch =>
    {
        branch.UseAuthentication();
        branch.UseAuthorization();
    });

app.MapControllers();

app.Run();
