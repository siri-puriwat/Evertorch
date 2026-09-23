using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Decides what a player may select and tells its owner when the selection changes (Gameplay Systems §6).
/// </summary>
public sealed class Targeting
{
    private readonly MessageSender m_sender;

    public Targeting(MessageSender sender)
    {
        m_sender = sender;
    }

    /// <summary>
    ///     In version 1 only live monsters the server has spawned to this client are targetable. A hidden entity
    ///     and a missing one are refused alike, so a refusal reveals nothing.
    /// </summary>
    public static bool IsTargetable(ClientSession session, EntityId target)
    {
        return session.Map != null
            && session.KnownEntities.Contains(target)
            && session.Map.TryGetMonster(target, out MonsterEntity? monster)
            && monster != null;
    }

    /// <summary>
    ///     Selects <paramref name="target" />, or clears the selection for entity 0. Returns false, changing nothing,
    ///     when the target may not be selected.
    /// </summary>
    public bool TrySelect(ClientSession session, EntityId target)
    {
        if (session.Player == null)
        {
            return false;
        }

        if (target != default && !IsTargetable(session, target))
        {
            return false;
        }

        SetTarget(session, session.Player, target);
        return true;
    }

    /// <summary>
    ///     Drops the player's target when it is <paramref name="entity" />; used when that entity leaves the
    ///     player's view or the world.
    /// </summary>
    public void ClearIfTargeting(ClientSession session, EntityId entity)
    {
        if (session.Player != null && session.Player.Target == entity && entity != default)
        {
            SetTarget(session, session.Player, default);
        }
    }

    private void SetTarget(ClientSession session, PlayerEntity player, EntityId target)
    {
        if (player.Target == target)
        {
            return;
        }

        player.Target = target;
        m_sender.Send(session.Connection, new TargetChanged(player.Id, target));
    }
}
}
