using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Domain.Interfaces;
using Shared.Infrastructure;
using Users.Application.Interfaces;
using Users.Infrastructure.Notifications;
using Users.Infrastructure.Repositories;

namespace Users.Infrastructure;

public static class Extensions
{
    public static IServiceCollection AddUsersInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthRequestRepository, AuthRequestRepository>();
        services.AddScoped<IDatabaseMigrator, EfCoreDatabaseMigrator<UserDbContext>>();

        services.AddVerificationCodeDelivery(configuration);

        services.AddSharedInfrastructure();

        return services;
    }

    /// <summary>
    ///     Picks the verification-code delivery channel by environment: the log-only sender on a developer
    ///     machine (Development, and the Local profile the IDE run configurations use), real SMTP everywhere
    ///     else. Stage and Production always get SMTP — no configuration switch can put the development
    ///     sender there.
    /// </summary>
    private static IServiceCollection AddVerificationCodeDelivery(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        services.AddScoped<IVerificationCodeSender>(provider =>
            IsDeveloperEnvironment(provider.GetRequiredService<IHostEnvironment>())
                ? ActivatorUtilities.CreateInstance<DevelopmentVerificationCodeSender>(provider)
                : ActivatorUtilities.CreateInstance<SmtpVerificationCodeSender>(provider));

        return services;
    }

    private static bool IsDeveloperEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.EnvironmentName == Shared.Environments.Local;
}
