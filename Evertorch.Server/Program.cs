using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        // Only a failure before the host exists gets here, and nothing has reported it yet.
        catch (Exception exception)
        {
            Console.Error.WriteLine($"The server could not start: {exception}");
            exitCode = 1;
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
                // Not Run, which disposes the host on its way out, before the outcome can be read.
                host.Start();
                host.WaitForShutdown();
                exitCode = host.Services.GetRequiredService<ServerLifetimeService>().HasFailed ? 1 : 0;
            }
            // The host has already logged every failure to start or stop. Bad content or configuration, a port
            // already bound, or a database that refuses the server is an operator's to fix, so the process exits with
            // a failure code instead of crashing with a second, unhandled copy of the report.
            catch (ContentLoadException)
            {
                isContentUnusable = true;
                exitCode = 1;
            }
            catch (Exception)
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
        Console.Error.WriteLine($"The server has no usable content package (environment: {environmentName}).");
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
