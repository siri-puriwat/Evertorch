using System.Collections.Generic;
using System.Threading.Tasks;

namespace Evertorch.Server
{
/// <summary>
///     The one entry point for operator commands. The development console calls it today and protected endpoints will
///     call it later; neither reaches into the world directly. The player protocol never exposes it. Every method takes
///     the actor, for authorization and the audit trail (System Architecture §10).
/// </summary>
public interface IAdminCommandService
{
    ServerStatus GetStatus(AdminActor actor);

    IReadOnlyList<PlayerSummary> GetPlayers(AdminActor actor);

    /// <summary>
    ///     Queues a checkpoint for every character in the world or in its grace period (Persistence §6). The tick thread
    ///     does it; the task ends with the number queued.
    /// </summary>
    Task<int> SaveAsync(AdminActor actor);

    /// <summary>
    ///     Stops the server the way Ctrl+C does. Every client is told <c>Maintenance</c>, with
    ///     <paramref name="reason" />, cut to what a notice carries, as its text.
    /// </summary>
    void Shutdown(AdminActor actor, string reason);
}
}
