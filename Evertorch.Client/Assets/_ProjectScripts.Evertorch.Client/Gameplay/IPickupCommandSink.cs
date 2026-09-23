using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Where the local pickup sends its request; the server decides and commits.
/// </summary>
public interface IPickupCommandSink
{
    /// <summary>
    ///     Returns the command sequence the request carried, so a refusal of it can be recognised; 0 when nothing was
    ///     sent.
    /// </summary>
    uint SendPickup(EntityId drop);
}
}
