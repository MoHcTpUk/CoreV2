using Identity.Application;
using Identity.Infrastructure;
using Identity.WebApi.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Shared.Api;
using Users.Api.Services;
using Environments = Shared.Environments;

namespace Identity.WebApi;

public static class Extensions
{
    public static IServiceCollection AddIdentityWebApi(this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddSharedApi(configuration);

        services.AddIdentityInfrastructure(configuration);
        services.AddIdentityApplication(configuration);
        
        // Identity.WebApi уже регистрирует Shared.Api (NATS + CommandBus).
        // Поэтому Users.Api подключаем только как сервисы поверх CommandBus, без повторного AddSharedApi().
        services.AddScoped<IUserService, UserService>();

        // Development и Local — профили машины разработчика (Program.cs трактует их так же).
        // Только они получают самоподписанные development-сертификаты OpenIddict, которые
        // генерируются заново при каждом старте. Stage/Production обязаны принести реальный
        // ключевой материал, иначе рестарт контейнера обнуляет все выданные токены.
        var isDeveloperProfile = environment.IsDevelopment() ||
                                 environment.EnvironmentName == Environments.Local;

        // Загружаем до регистрации чего-либо: при отсутствии сертификатов старт должен
        // упасть здесь с внятным текстом, а не поднять сервис с эфемерными ключами.
        var signingCertificate = isDeveloperProfile
            ? null
            : CertificateLoader.Load(configuration, "OpenIddict:SigningCertificate", "token signing");

        var encryptionCertificate = isDeveloperProfile
            ? null
            : CertificateLoader.Load(configuration, "OpenIddict:EncryptionCertificate", "token encryption");

        // Key ring DataProtection должен переживать рестарт и быть общим для реплик:
        // от него зависит UseReferenceRefreshTokens() ниже.
        services.AddIdentityDataProtection(configuration, isDeveloperProfile, encryptionCertificate);

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>();
            })
            .AddServer(options =>
            {
                var openIddictIssuer = configuration["OpenIddict:Issuer"] ?? "http://identity-webapi:8080/";

                options.SetIssuer(new Uri(openIddictIssuer));
                options.SetTokenEndpointUris("/connect/token")
                    .SetUserinfoEndpointUris("/connect/userinfo");

                options.AllowPasswordFlow()
                    .AllowRefreshTokenFlow();

                // Собственный grant для подтверждённого auth request: клиент предъявляет одноразовый
                // ticket, выпущенный Identity, а не пароль. Пароль по шине больше не ходит
                // (см. Identity.WebApi/Security/AuthRequestGrant.cs).
                options.AllowCustomFlow(AuthRequestGrant.GrantType);

                options.DisableAccessTokenEncryption();

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Roles);

                if (isDeveloperProfile)
                {
                    // Самоподписанный сертификат в профиле пользователя: переживает рестарт
                    // на машине разработчика и пересоздаётся в контейнере локального стенда.
                    options.AddDevelopmentSigningCertificate();

                    var developerSecret = configuration["OpenIddict:SigningKey"];

                    if (!string.IsNullOrWhiteSpace(developerSecret))
                        options.AddEncryptionKey(
                            new SymmetricSecurityKey(SHA256.HashData(Encoding.UTF8.GetBytes(developerSecret))));
                    else
                        options.AddDevelopmentEncryptionCertificate();
                }
                else
                {
                    // Ключевой материал приходит извне (docker secret / X509-хранилище) и
                    // переживает и рестарт, и переезд на другую реплику.
                    options.AddSigningCertificate(signingCertificate!);
                    options.AddEncryptionCertificate(encryptionCertificate!);
                }

                options.UseReferenceRefreshTokens();

                options.UseAspNetCore()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserinfoEndpointPassthrough()
                    .DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        services.AddAuthorization();

        return services;
    }
}
