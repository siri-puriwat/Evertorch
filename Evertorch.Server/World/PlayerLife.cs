using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Brings a dead player back on request (Gameplay Systems §10.1): at its map's spawn point, with full HP and SP,
///     told to every client that knows it before visibility is recomputed, then its HP to the owner.
/// </summary>
public sealed class PlayerLife
{
    private readonly SessionRegistry m_sessions;
    private readonly MessageSender m_sender;

    public PlayerLife(SessionRegistry sessions, MessageSender sender)
    {
        m_sessions = sessions;
        m_sender = sender;
    }

    /// <summary>
    ///     Revives the session's dead player. Returns false, changing nothing, while it is alive.
    /// </summary>
    public bool TryRespawn(ClientSession session, uint tick)
    {
        PlayerEntity? player = session.Player;
        MapInstance? map = session.Map;
        if (player == null || map == null || !player.IsDead)
        {
            return false;
        }

        player.Position = map.Definition.SpawnPosition;
        player.Facing = MovementModel.NormalizeOrZero(map.Definition.SpawnFacing.X, map.Definition.SpawnFacing.Z);
        player.VelocityX = 0f;
        player.VelocityY = 0f;
        player.VelocityZ = 0f;
        player.StateFlags = EntityStateFlags.None;
        player.CurrentHealth = player.MaxHealth;
        player.CurrentSpirit = player.MaxSpirit;
        player.Target = default;
        player.Combat.IsAutoAttacking = false;
        player.Combat.EndSwing();
        // Inputs sent while dead must not carry over into the new life.
        session.Input?.Halt();

        var revived = new EntityRevived(player.Id, player.Position, player.Facing, tick);
        foreach (ClientSession other in m_sessions.Sessions)
        {
            if (other.State == SessionState.InWorld && other.Map == map && other.Knows(player.Id))
            {
                m_sender.Send(other.Connection, revived);
            }
        }

        m_sender.Send(session.Connection, new CharacterHealth((uint)player.CurrentHealth, (uint)player.MaxHealth));
        return true;
    }
}
}
