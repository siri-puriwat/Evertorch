using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Evertorch.Server
{
/// <summary>
///     The lifetime of a Kestrel host inside the server: the server's own lifetime answers Ctrl+C, and this host only
///     starts and stops when the server says so.
/// </summary>
internal sealed class EmbeddedHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
}
