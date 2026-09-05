using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Sample.Application;

public static class Extensions
{
    public static IServiceCollection AddSampleApplication(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSharedApplication(configuration);

        return services;
    }
}
