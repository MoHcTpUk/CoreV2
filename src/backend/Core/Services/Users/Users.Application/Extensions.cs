using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application;
using Users.Application.Options;

namespace Users.Application;

public static class Extensions
{
    public static IServiceCollection AddUsersApplication(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AuthRequestOptions>(configuration.GetSection(AuthRequestOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSharedApplication(configuration);

        return services;
    }
}
