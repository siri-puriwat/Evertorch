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
    ///     Only live monsters the server has spawned to this client can be attacked or struck: there is no PvP. A hidden
    ///     entity and a missing one are refused alike, so a refusal reveals nothing.
    /// </summary>
    public static bool IsAttackable(ClientSession session, EntityId target)
    {
        return session.Map != null
            && session.KnownEntities.Contains(target)
            && session.Map.TryGetMonster(target, out MonsterEntity? monster)
            && monster != null
            && !monster.IsDead;
    }

    /// <summary>
    ///     A live player other than its own that the server has spawned to this client on its map instance, which it
    ///     may select and an ally skill may name (Gameplay Systems §6, §9).
    /// </summary>
    public static bool IsSelectablePlayer(ClientSession session, EntityId target)
    {
        return session.Map != null
            && target != session.Player?.Id
            && session.KnownEntities.Contains(target)
            && session.Map.TryGetPlayer(target, out PlayerEntity? player)
            && player != null
            && !player.IsDead;
    }

    /// <summary>
    ///     Selects <paramref name="target" />, a monster it could attack or a player it knows, or clears the selection
    ///     for entity 0. Returns false, changing nothing, when the target may not be selected. Selecting ends an
    ///     auto-attack and never starts one.
    /// </summary>
    public bool TrySelect(ClientSession session, EntityId target)
    {
        if (session.Player == null)
        {
            return false;
        }

        if (target != default && !IsAttackable(session, target) && !IsSelectablePlayer(session, target))
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

    /// <summary>
    ///     Selects <paramref name="target" /> and keeps attacking it. Returns false, changing nothing, when the
    ///     target may not be attacked.
    /// </summary>
    public bool TryAttack(ClientSession session, EntityId target)
    {
        if (session.Player == null
            || target == default
            || !IsAttackable(session, target)
            || !TrySelect(session, target))
        {
            return false;
        }

        session.Player.Combat.IsAutoAttacking = true;
        return true;
    }

    private void SetTarget(ClientSession session, PlayerEntity player, EntityId target)
    {
        if (player.Target == target)
        {
            return;
        }

        // Selecting another target, or none, ends auto-attack; a swing already begun still resolves.
        player.Combat.IsAutoAttacking = false;
        player.Target = target;
        m_sender.Send(session.Connection, new TargetChanged(player.Id, target));
    }
}
}
