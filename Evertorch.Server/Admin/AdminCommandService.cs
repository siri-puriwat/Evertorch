using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Evertorch.Server
{
/// <summary>
///     Read-only commands answer from the published status, so they are safe from any thread and cost the tick nothing.
///     A command that acts on the world goes through the <see cref="AdminQueue" /> to the tick thread, and a shutdown
///     goes to the host.
/// </summary>
public sealed class AdminCommandService : IAdminCommandService
{
    private readonly StatusPublisher m_status;
    private readonly AdminQueue m_queue;
    private readonly ShutdownRequest m_shutdown;
    private readonly IHostApplicationLifetime m_lifetime;
    private readonly AuditLog m_audit;

    public AdminCommandService(
        StatusPublisher status,
        AdminQueue queue,
        ShutdownRequest shutdown,
        IHostApplicationLifetime lifetime,
        AuditLog audit)
    {
        m_status = status;
        m_queue = queue;
        m_shutdown = shutdown;
        m_lifetime = lifetime;
        m_audit = audit;
    }

    public ServerStatus GetStatus(AdminActor actor)
    {
        return m_status.Current;
    }

    public IReadOnlyList<PlayerSummary> GetPlayers(AdminActor actor)
    {
        return m_status.Current.Players;
    }

    public Task<int> SaveAsync(AdminActor actor)
    {
        return m_queue.RequestSave(actor);
    }

    // Called from the console thread, so it works even when the tick thread hangs. The audit event comes first, so the
    // reason is on record whatever happens while stopping.
    public void Shutdown(AdminActor actor, string reason)
    {
        m_shutdown.Set(reason);
        m_audit.OperatorShutdown(actor, m_shutdown.Message);
        m_lifetime.StopApplication();
    }
}
}
