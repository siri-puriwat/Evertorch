using System.Collections.Generic;

namespace Evertorch.Server
{
/// <summary>
///     Read-only commands answer from the published status, so they are safe from any thread and cost the tick nothing.
/// </summary>
public sealed class AdminCommandService : IAdminCommandService
{
    private readonly StatusPublisher m_status;

    public AdminCommandService(StatusPublisher status)
    {
        m_status = status;
    }

    public ServerStatus GetStatus()
    {
        return m_status.Current;
    }

    public IReadOnlyList<PlayerSummary> GetPlayers()
    {
        return m_status.Current.Players;
    }
}
}
