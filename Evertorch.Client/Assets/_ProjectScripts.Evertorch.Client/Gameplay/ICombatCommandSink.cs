using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Where the local auto-attack sends its requests; the server decides what happens.
/// </summary>
public interface ICombatCommandSink
{
    /// <summary>
    ///     Returns the command sequence the request carried, so a refusal of it can be recognised; 0 when nothing was
    ///     sent.
    /// </summary>
    uint SendAttack(EntityId target);

    void SendCancel();
}
}
