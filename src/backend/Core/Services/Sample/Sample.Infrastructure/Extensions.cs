using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample.Domain;
using Sample.Infrastructure.Repositories;
using Shared.Domain.Interfaces;
using Shared.Infrastructure;

namespace Sample.Infrastructure;

public static class Extensions
{
    public static IServiceCollection AddSampleInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ISampleRepository, SampleRepository>();
        services.AddScoped<IDatabaseMigrator, EfCoreDatabaseMigrator<SampleDbContext>>();

        services.AddSharedInfrastructure();

        return services;
    }
}
