using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public static class ServerHost
{
    /// <summary>
    /// Builds the server composition. Configuration comes from <c>appsettings.json</c> and
    /// <c>appsettings.{Environment}.json</c> under <paramref name="contentRootPath"/>, then environment variables,
    /// then the command line; later sources win.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args, string contentRootPath)
    {
        HostApplicationBuilderSettings settings = new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = contentRootPath,
        };
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(settings);

        builder.Services
            .AddOptions<SimulationOptions>()
            .Bind(builder.Configuration.GetSection(SimulationOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SimulationOptions>, SimulationOptionsValidator>();

        builder.Services.AddSingleton<IMonotonicClock, StopwatchClock>();
        builder.Services.AddSingleton<ITickObserver, TickLogObserver>();
        builder.Services.AddSingleton<TickPipeline>();
        builder.Services.AddSingleton<FixedStepLoop>();
        builder.Services.AddSingleton<ServerLifetimeService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<ServerLifetimeService>());

        return builder;
    }
}
}
