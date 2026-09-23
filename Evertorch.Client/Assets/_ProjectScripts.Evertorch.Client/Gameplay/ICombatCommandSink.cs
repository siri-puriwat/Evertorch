using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Where the local auto-attack sends its requests; the server decides what happens.
/// </summary>
public interface ICombatCommandSink
{
    void SendAttack(EntityId target);

    void SendCancel();
}
}
