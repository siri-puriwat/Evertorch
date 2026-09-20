using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public static class Program
{
    public static int Main(string[] args)
    {
        using IHost host = ServerHost.CreateBuilder(args, AppContext.BaseDirectory).Build();
        try
        {
            host.Run();
        }
        catch (Exception exception) when (exception is ContentLoadException || exception is OptionsValidationException)
        {
            // The host has already logged it. Bad content or configuration is an operator error, so exit with a
            // failure code instead of crashing with a second, unhandled copy of the same report.
            return 1;
        }

        return host.Services.GetRequiredService<ServerLifetimeService>().HasFaulted ? 1 : 0;
    }
}
}
