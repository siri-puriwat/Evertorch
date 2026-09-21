using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public static class Program
{
    public static int Main(string[] args)
    {
        int exitCode;
        try
        {
            exitCode = Run(args);
        }
        catch
        {
            KeepWindowOpenIfOwned();
            throw;
        }

        if (exitCode != 0)
        {
            KeepWindowOpenIfOwned();
        }

        return exitCode;
    }

    private static int Run(string[] args)
    {
        string environmentName;
        bool isContentUnusable = false;
        int exitCode;
        using (IHost host = ServerHost.CreateBuilder(args, AppContext.BaseDirectory).Build())
        {
            environmentName = host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName;
            try
            {
                host.Run();
                exitCode = host.Services.GetRequiredService<ServerLifetimeService>().HasFaulted ? 1 : 0;
            }
            // In both cases the host has already logged the problem. Bad content or configuration is an operator
            // error, so exit with a failure code instead of crashing with a second, unhandled copy of the report.
            catch (ContentLoadException)
            {
                isContentUnusable = true;
                exitCode = 1;
            }
            catch (OptionsValidationException)
            {
                exitCode = 1;
            }
        }

        // Only now: disposing the host flushes the console logger, so the hint follows the host's own report.
        if (isContentUnusable)
        {
            WriteContentHint(environmentName);
        }

        return exitCode;
    }

    private static void WriteContentHint(string environmentName)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("The server has no usable content package (environment: " + environmentName + ").");
        Console.Error.WriteLine(
            "For local development start it with scripts\\run-server.cmd, or with the 'Development' launch profile"
            + " (DOTNET_ENVIRONMENT=Development);");
        Console.Error.WriteLine(
            "both read the package that 'dotnet run --project Evertorch.Tools -- content build' writes to"
            + " artifacts/content/server.");
        Console.Error.WriteLine("A deployed server needs its package at Content:ServerPackagePath.");
    }

    // Started from Explorer, the server gets a console window of its own that closes the moment it exits, taking
    // the reason with it. Only in that case is there nobody else to read the output, so only then does it wait.
    private static void KeepWindowOpenIfOwned()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || !IsOnlyProcessOnConsole())
        {
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("The server stopped. Press Enter to close this window.");
        Console.ReadLine();
    }

    private static bool IsOnlyProcessOnConsole()
    {
        uint[] processes = new uint[2];
        return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
}
