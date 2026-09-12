using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application;
using Identity.Application.Options;
using Identity.Application.Services;

namespace Identity.Application;

public static class Extensions
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AuthTicketOptions>(configuration.GetSection(AuthTicketOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IAuthTicketService, AuthTicketService>();
        
        services.AddSharedApplication(configuration);

        return services;
    }
}
