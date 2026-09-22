using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Evertorch.Server
{
/// <summary>
///     Runs the development console on its own thread for as long as the host runs.
/// </summary>
public sealed class ConsoleCommandService : IHostedService, IDisposable
{
    private readonly AdminConsole m_console;
    private readonly CancellationTokenSource m_stop = new();

    public ConsoleCommandService(AdminConsole console)
    {
        m_console = console;
    }

    public void Dispose()
    {
        m_stop.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // A background thread: a blocked ReadLine cannot be cancelled and must not keep the process alive.
        var thread = new Thread(() => m_console.Run(Console.In, Console.Out, m_stop.Token))
        {
            Name = "AdminConsole",
            IsBackground = true
        };
        thread.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        m_stop.Cancel();
        return Task.CompletedTask;
    }
}
}
