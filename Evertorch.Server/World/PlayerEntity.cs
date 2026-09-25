using System;
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

    public JobDefinitionId Job { get; }

    public int Level { get; set; }

    /// <summary>
    ///     Experience toward the next level; 0 at the job's level cap.
    /// </summary>
    public long Experience { get; set; }

    /// <summary>
    ///     The character's derived statistics from the rules; recalculated only when their inputs change.
    /// </summary>
    public DerivedStats Stats { get; private set; }

    public PrimaryStats Primary { get; }

    public Regeneration Regeneration { get; private set; }

    public int MaxSpirit { get; private set; }

    public int CurrentSpirit { get; set; }

    /// <summary>
    ///     When the next HP and SP regeneration steps fall due; <see cref="long.MinValue" /> until the regeneration
    ///     phase first sees the player.
    /// </summary>
    public long NextHealthRegenerationMs { get; set; } = long.MinValue;

    public long NextSpiritRegenerationMs { get; set; } = long.MinValue;

    public override EntityKind Kind => EntityKind.Player;

    public override string DefinitionId => Job.Value;

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
