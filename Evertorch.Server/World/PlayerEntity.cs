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
    ///     When the next HP and SP regeneration steps fall due; <see cref="long.MinValue" /> until the regeneration
    ///     phase first sees the player.
    /// </summary>
    public long NextHealthRegenerationMs { get; set; } = long.MinValue;

    public long NextSpiritRegenerationMs { get; set; } = long.MinValue;

    /// <summary>
    ///     The status effects on the player, in the order they started; they are not persisted (Gameplay Systems §9.1).
    /// </summary>
    public IReadOnlyList<ActiveStatusEffect> StatusEffects => m_statusEffects;

    public override EntityKind Kind => EntityKind.Player;

    public override string DefinitionId => Job.Value;

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
