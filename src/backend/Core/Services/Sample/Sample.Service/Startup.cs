using Microsoft.Extensions.Hosting;
using Sample.Application;
using Sample.Infrastructure;
using Shared.Service;

namespace Sample.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = HostBuilderHelper
            .CreateHostBuilder(args, (services, config) =>
            {
                services.AddSampleInfrastructure(config);
                services.AddSampleApplication(config);
            }).Build();

        await host.RunAsync();
    }
}
