using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     A player's authoritative presence on one map.
/// </summary>
public sealed class PlayerEntity : WorldEntity
{
    private readonly List<ActiveStatusEffect> m_statusEffects = new();
    private readonly Dictionary<SkillDefinitionId, int> m_skills = new();

    public PlayerEntity(
        EntityId id,
        CharacterId character,
        string name,
        ConnectionId owner,
        JobDefinitionId job,
        WorldPosition position,
        WorldDirection facing,
        float movementSpeed,
        int level,
        PrimaryStats primary,
        DerivedStats stats,
        Regeneration regeneration,
        float attackRange)
        : base(id, position, facing, movementSpeed, stats.MaxHp, attackRange)
    {
        Character = character;
        Name = name;
        Owner = owner;
        Job = job;
        Level = level;
        Primary = primary;
        Stats = stats;
        Regeneration = regeneration;
        MaxSpirit = stats.MaxSp;
        CurrentSpirit = stats.MaxSp;
    }

    public CharacterId Character { get; }

    /// <summary>
    ///     The character's name from its load, which everyone who sees the player reads (Network Protocol §6).
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     The connection that controls the player; default while none does.
    /// </summary>
    public ConnectionId Owner { get; set; }

    /// <summary>
    ///     The job, which only a committed job change sets (Gameplay Systems §6.1); an observer draws the body it names.
    /// </summary>
    public JobDefinitionId Job { get; private set; }

    public int Level { get; set; }

    /// <summary>
    ///     Experience toward the next level; 0 at the job's level cap.
    /// </summary>
    public long Experience { get; set; }

    /// <summary>
    ///     The character's derived statistics from the rules; recalculated only when their inputs change.
    /// </summary>
    public DerivedStats Stats { get; private set; }

    /// <summary>
    ///     The stored primary statistics, before status effects and equipment; they change only through
    ///     <see cref="SetPrimary" /> (Gameplay Systems §2).
    /// </summary>
    public PrimaryStats Primary { get; private set; }

    public int JobLevel { get; set; } = 1;

    /// <summary>
    ///     Job experience toward the next job level; 0 at the job's cap.
    /// </summary>
    public long JobExperience { get; set; }

    /// <summary>
    ///     The skills the character has learned, each at its level (Gameplay Systems §9); a skill not learned is absent.
    /// </summary>
    public IReadOnlyDictionary<SkillDefinitionId, int> Skills => m_skills;

    public Regeneration Regeneration { get; private set; }

    public int MaxSpirit { get; private set; }

    public int CurrentSpirit { get; set; }

    /// <summary>
    ///     What the worn weapon adds (Gameplay Systems §11.1); null while the weapon slot is empty. It changes when an
    ///     equip or unequip is committed, and with the character's load.
    /// </summary>
    public ItemEquipment? Weapon { get; set; }

    /// <summary>
    ///     What the worn armor adds; null while the armor slot is empty.
    /// </summary>
    public ItemEquipment? Armor { get; set; }

    /// <summary>
    ///     What the worn weapon and armor add in combat, refine bonuses included (Gameplay Systems §11.1); only
    ///     <see cref="WornEquipment.Dress(PlayerEntity, ServerContent, CharacterInventory)" /> sets it.
    /// </summary>
    public WornEquipment Equipment { get; set; } = WornEquipment.None;

    /// <summary>
    ///     What the worn rows' cards add, summed (Gameplay Systems §11.1): their statistics and maxima, which the
    ///     statistics take, and the boss modifiers, which combat takes; their attack, magic attack, and defense are in
    ///     <see cref="Equipment" />. Only <see cref="WornEquipment" />'s <c>Dress</c> sets it.
    /// </summary>
    public CardBonus CardBonus { get; set; } = CardBonus.None;

    /// <summary>
    ///     The element of the player's swings and weapon skills (Gameplay Systems §9.1): the active status's that sets
    ///     one, a converter's, else Neutral.
    /// </summary>
    public Element AttackElement
    {
        get
        {
            foreach (ActiveStatusEffect effect in m_statusEffects)
            {
                if (effect.AttackElement.HasValue)
                {
                    return effect.AttackElement.Value;
                }
            }

            return Element.Neutral;
        }
    }

    /// <summary>
    ///     The element the worn armor's card gives the player (Gameplay Systems §11.7), else Neutral; it follows
    ///     <see cref="CardBonus" />, which the equipment helper sets at the load and at every change of what is worn.
    /// </summary>
    public Element ArmorElement => CardBonus.ArmorElement;

    /// <summary>
    ///     Set by a job change: the next visibility pass sends the player's spawn, with its new job, to every session
    ///     that already sees it, and clears the flag (Network Protocol §9).
    /// </summary>
    public bool RespawnPending { get; set; }

    /// <summary>
    ///     A trade is open: the player is held still, as through a swing or a cast (Gameplay Systems §5, §16).
    /// </summary>
    public bool IsTrading { get; set; }

    /// <summary>
    ///     How often the player has died since it entered the world, so an answer that returns after a death and a respawn
    ///     knows the death came between (Gameplay Systems §11.2).
    /// </summary>
    public int Deaths { get; set; }

    /// <summary>
    ///     The item of the weapon the player wears, which everyone near sees in its hand; null while the weapon slot is
    ///     empty (Gameplay Systems §11.1).
    /// </summary>
    public ItemDefinitionId? WornWeapon { get; private set; }

    /// <summary>
    ///     Set when <see cref="WornWeapon" /> changes: the next visibility pass tells every session that already sees the
    ///     player, and clears the flag (Network Protocol §9).
    /// </summary>
    public bool AppearancePending { get; set; }

    /// <summary>
    ///     When the next HP and SP regeneration steps fall due; <see cref="long.MinValue" /> until the regeneration
    ///     phase first sees the player.
    /// </summary>
    public long NextHealthRegenerationMs { get; set; } = long.MinValue;

    public long NextSpiritRegenerationMs { get; set; } = long.MinValue;

    /// <summary>
    ///     The status effects on the player, in the order they started; the checkpoint saves them with their time left
    ///     (Gameplay Systems §9.1).
    /// </summary>
    public IReadOnlyList<ActiveStatusEffect> StatusEffects => m_statusEffects;

    public override EntityKind Kind => EntityKind.Player;

    public override string DefinitionId => Job.Value;

    /// <summary>
    ///     Keeps the worn weapon's item; only a change of the item itself, never an armor or the same item again, is
    ///     announced.
    /// </summary>
    public void SetWornWeapon(ItemDefinitionId? item)
    {
        if (Nullable.Equals(WornWeapon, item))
        {
            return;
        }

        WornWeapon = item;
        AppearancePending = true;
    }

    /// <summary>
    ///     Adds <paramref name="effect" />, or renews the active effect of the same status to its end. True when it was
    ///     not active.
    /// </summary>
    public bool StartStatusEffect(ActiveStatusEffect effect)
    {
        for (int index = 0; index < m_statusEffects.Count; index++)
        {
            if (m_statusEffects[index].Status == effect.Status)
            {
                m_statusEffects[index] = effect;
                return false;
            }
        }

        m_statusEffects.Add(effect);
        return true;
    }

    /// <summary>
    ///     Ends the effects whose time is up at <paramref name="nowMs" />; returns how many ended.
    /// </summary>
    public int EndStatusEffectsDueBy(long nowMs)
    {
        return m_statusEffects.RemoveAll(effect => effect.EndMs <= nowMs);
    }

    /// <summary>
    ///     Ends every effect that sets an attack element but one of <paramref name="kept" />; returns how many ended.
    /// </summary>
    public int EndAttackElementEffects(StatusDefinitionId? kept = null)
    {
        return m_statusEffects.RemoveAll(effect => effect.AttackElement.HasValue && effect.Status != kept);
    }

    public int EndAllStatusEffects()
    {
        int count = m_statusEffects.Count;
        m_statusEffects.Clear();
        return count;
    }

    /// <summary>
    ///     Makes the player a first job at job level 1 with job experience 0, keeping its base level, statistics, and
    ///     learned skills (Gameplay Systems §2.1); the caller derives the statistics again.
    /// </summary>
    public void ChangeJob(JobDefinitionId job)
    {
        Job = job;
        JobLevel = 1;
        JobExperience = 0;
    }

    /// <summary>
    ///     Replaces the primary statistics; the caller derives the statistics again.
    /// </summary>
    public void SetPrimary(PrimaryStats primary)
    {
        Primary = primary;
    }

    /// <summary>
    ///     Learns <paramref name="skill" /> at <paramref name="level" />; level 0 forgets it.
    /// </summary>
    public void SetSkillLevel(SkillDefinitionId skill, int level)
    {
        if (level <= 0)
        {
            m_skills.Remove(skill);
        }
        else
        {
            m_skills[skill] = level;
        }
    }

    public void ForgetSkills()
    {
        m_skills.Clear();
    }

    /// <summary>
    ///     Takes recalculated statistics, keeping the current HP and SP within the new maximums (Gameplay Systems §2).
    /// </summary>
    public void ApplyStats(DerivedStats stats, Regeneration regeneration)
    {
        Stats = stats;
        Regeneration = regeneration;
        MaxHealth = stats.MaxHp;
        MaxSpirit = stats.MaxSp;
        CurrentHealth = Math.Min(CurrentHealth, MaxHealth);
        CurrentSpirit = Math.Min(CurrentSpirit, MaxSpirit);
    }
}
}
