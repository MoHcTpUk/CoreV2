using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample.Api.Services;
using Shared.Api;

namespace Sample.Api;

public static class Extensions
{
    public static IServiceCollection AddSampleApi(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSharedApi(configuration);

        services.AddScoped<ISampleService, SampleService>();

        return services;
    }
}
