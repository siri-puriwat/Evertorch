using System.Collections.Generic;

namespace Evertorch.Server
{
/// <summary>
/// The one entry point for operator commands. The development console calls it today and protected endpoints will
/// call it later; neither reaches into the world directly. The player protocol never exposes it.
/// </summary>
public interface IAdminCommandService
{
    ServerStatus GetStatus();

    IReadOnlyList<PlayerSummary> GetPlayers();
}
}
