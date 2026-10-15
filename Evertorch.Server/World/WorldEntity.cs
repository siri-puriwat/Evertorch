using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Anything with an authoritative presence on a map: its identity, transform, and state flags. Only the tick
///     thread reads or writes it.
/// </summary>
public abstract class WorldEntity
{
    private const EntityStateFlags AilmentFlags = EntityStateFlags.Poisoned
        | EntityStateFlags.Stunned
        | EntityStateFlags.Frozen
        | EntityStateFlags.Asleep
        | EntityStateFlags.Silenced
        | EntityStateFlags.Blinded;

    private readonly List<ActiveStatusEffect> m_statusEffects = new();

    protected WorldEntity(
        EntityId id,
        WorldPosition position,
        WorldDirection facing,
        float movementSpeed,
        int maxHealth,
        float attackRange)
    {
        Id = id;
        Position = position;
        Facing = facing;
        MovementSpeed = movementSpeed;
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
        AttackRange = attackRange;
    }

    public EntityId Id { get; }

    public abstract EntityKind Kind { get; }

    /// <summary>
    ///     The definition clients present this entity with; its kind follows <see cref="Kind" />.
    /// </summary>
    public abstract string DefinitionId { get; }

    public WorldPosition Position { get; set; }

    public WorldDirection Facing { get; set; }

    public float VelocityX { get; set; }

    public float VelocityY { get; set; }

    public float VelocityZ { get; set; }

    public EntityStateFlags StateFlags { get; set; }

    /// <summary>
    ///     The entity this one has selected; the default value means none.
    /// </summary>
    public EntityId Target { get; set; }

    /// <summary>
    ///     World units per second after the movement rules were applied; never a value a client supplied.
    /// </summary>
    public float MovementSpeed { get; }

    public int MaxHealth { get; protected set; }

    public int CurrentHealth { get; set; }

    /// <summary>
    ///     How far, centre to centre on X/Z, a basic attack reaches.
    /// </summary>
    public float AttackRange { get; }

    public bool IsDead => (StateFlags & EntityStateFlags.Dead) != 0;

    public CombatState Combat { get; } = new();

    /// <summary>
    ///     The HP ratio clients may see: a monster's, for its health bar. Other kinds share none.
    /// </summary>
    public ushort SharedHealthPermille =>
        Kind == EntityKind.Monster ? HealthRatio.ToPermille(CurrentHealth, MaxHealth) : (ushort)0;

    /// <summary>
    ///     The status effects on the entity, in the order they started (Gameplay Systems §9.1): a player's every kind, a
    ///     monster's ailments alone. The checkpoint saves a player's with their time left.
    /// </summary>
    public IReadOnlyList<ActiveStatusEffect> StatusEffects => m_statusEffects;

    /// <summary>Whether a stun, a freeze, or a sleep holds the body.</summary>
    public bool IsHeld => HasAilment(Ailment.Stun) || HasAilment(Ailment.Freeze) || HasAilment(Ailment.Sleep);

    /// <summary>The state flag of each ailment (Network Protocol §6): Poisoned 0x0004 to Blinded 0x0080.</summary>
    public static EntityStateFlags FlagOf(Ailment ailment)
    {
        return ailment == Ailment.None ? EntityStateFlags.None : (EntityStateFlags)(1 << ((int)ailment + 1));
    }

    public bool HasAilment(Ailment ailment)
    {
        return (StateFlags & FlagOf(ailment)) != 0;
    }

    /// <summary>
    ///     Adds <paramref name="effect" />, or renews the active effect of the same status to its end. True when it was
    ///     not active.
    /// </summary>
    public bool StartStatusEffect(ActiveStatusEffect effect)
    {
        bool isNew = true;
        for (int index = 0; index < m_statusEffects.Count; index++)
        {
            if (m_statusEffects[index].Status == effect.Status)
            {
                m_statusEffects[index] = effect;
                isNew = false;
            }
        }

        if (isNew)
        {
            m_statusEffects.Add(effect);
        }

        SetAilmentFlags();
        return isNew;
    }

    /// <summary>
    ///     Ends the effects whose time is up at <paramref name="nowMs" />; returns how many ended.
    /// </summary>
    public int EndStatusEffectsDueBy(long nowMs)
    {
        return Ended(m_statusEffects.RemoveAll(effect => effect.EndMs <= nowMs));
    }

    /// <summary>
    ///     Ends every effect that sets an attack element but one of <paramref name="kept" />; returns how many ended.
    /// </summary>
    public int EndAttackElementEffects(StatusDefinitionId? kept = null)
    {
        return Ended(m_statusEffects.RemoveAll(effect => effect.AttackElement.HasValue && effect.Status != kept));
    }

    /// <summary>Ends the effects of the given ailments; returns how many ended.</summary>
    public int EndAilments(Predicate<Ailment> ended)
    {
        return Ended(m_statusEffects.RemoveAll(effect => effect.Ailment != Ailment.None && ended(effect.Ailment)));
    }

    public int EndAllStatusEffects()
    {
        return Ended(m_statusEffects.RemoveAll(_ => true));
    }

    /// <summary>
    ///     Sets when the poison of the effect at <paramref name="index" /> next drains, as its schedule moves on.
    /// </summary>
    public void SetNextTick(int index, long nextTickMs)
    {
        m_statusEffects[index] = m_statusEffects[index].WithNextTick(nextTickMs);
    }

    private int Ended(int count)
    {
        if (count > 0)
        {
            SetAilmentFlags();
        }

        return count;
    }

    // Everyone who knows the body sees its ailments, in the flags of its spawn and every snapshot (Gameplay Systems
    // §9.1).
    private void SetAilmentFlags()
    {
        EntityStateFlags flags = StateFlags & ~AilmentFlags;
        foreach (ActiveStatusEffect effect in m_statusEffects)
        {
            flags |= FlagOf(effect.Ailment);
        }

        StateFlags = flags;
    }
}
}
