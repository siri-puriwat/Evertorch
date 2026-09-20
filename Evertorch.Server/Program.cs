using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Evertorch.Server
{
public static class Program
{
    public static int Main(string[] args)
    {
        using IHost host = ServerHost.CreateBuilder(args, AppContext.BaseDirectory).Build();
        host.Run();
        return host.Services.GetRequiredService<ServerLifetimeService>().HasFaulted ? 1 : 0;
    }
}
}
