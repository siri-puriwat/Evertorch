using System.Collections.Generic;

namespace Evertorch.Game
{
public sealed class MonsterDefinition
{
    public MonsterDefinition(
        MonsterDefinitionId id,
        string displayName,
        int level,
        int hp,
        int physicalAttack,
        int physicalDefense,
        int hit,
        int flee,
        int magicAttack,
        double baseSpeed,
        double attackRange,
        int attackIntervalMs,
        MonsterBehavior behavior,
        double perceptionRadius,
        double leashRadius,
        double roamRadius,
        int idlePauseMinMs,
        int idlePauseMaxMs,
        int scanIntervalMs,
        double keepDistance,
        int baseExperience,
        int jobExperience,
        IReadOnlyList<MonsterDrop> drops,
        IReadOnlyList<MonsterSkill> skills)
    {
        Id = id;
        DisplayName = displayName;
        Level = level;
        Hp = hp;
        PhysicalAttack = physicalAttack;
        PhysicalDefense = physicalDefense;
        Hit = hit;
        Flee = flee;
        MagicAttack = magicAttack;
        BaseSpeed = baseSpeed;
        AttackRange = attackRange;
        AttackIntervalMs = attackIntervalMs;
        Behavior = behavior;
        PerceptionRadius = perceptionRadius;
        LeashRadius = leashRadius;
        RoamRadius = roamRadius;
        IdlePauseMinMs = idlePauseMinMs;
        IdlePauseMaxMs = idlePauseMaxMs;
        ScanIntervalMs = scanIntervalMs;
        KeepDistance = keepDistance;
        BaseExperience = baseExperience;
        JobExperience = jobExperience;
        Drops = drops;
        Skills = skills;
    }

    public MonsterDefinitionId Id { get; }

    public string DisplayName { get; }

    public int Level { get; }

    public int Hp { get; }

    public int PhysicalAttack { get; }

    public int PhysicalDefense { get; }

    public int Hit { get; }

    public int Flee { get; }

    /// <summary>What its casts roll between 80 % and 120 %; 0 when it casts nothing magical.</summary>
    public int MagicAttack { get; }

    /// <summary>World units per second before any movement rule is applied.</summary>
    public double BaseSpeed { get; }

    public double AttackRange { get; }

    public int AttackIntervalMs { get; }

    public MonsterBehavior Behavior { get; }

    public double PerceptionRadius { get; }

    public double LeashRadius { get; }

    /// <summary>Roam targets are drawn within this distance of the monster's home.</summary>
    public double RoamRadius { get; }

    /// <summary>The shortest pause before the next roam; both bounds are inclusive.</summary>
    public int IdlePauseMinMs { get; }

    public int IdlePauseMaxMs { get; }

    /// <summary>The cadence of the AI's decisions: acquiring, scanning, re-pathing, and the leash.</summary>
    public int ScanIntervalMs { get; }

    /// <summary>
    ///     A target nearer than this makes the monster walk away at a decision (Gameplay Systems §10); 0 keeps no
    ///     distance. Always below the attack range.
    /// </summary>
    public double KeepDistance { get; }

    /// <summary>
    ///     The experience shared by the characters that damaged the monster when it dies (Gameplay Systems §2.1); 0
    ///     gives none.
    /// </summary>
    public int BaseExperience { get; }

    /// <summary>
    ///     The job experience shared the same way as <see cref="BaseExperience" />; 0 gives none.
    /// </summary>
    public int JobExperience { get; }

    public IReadOnlyList<MonsterDrop> Drops { get; }

    /// <summary>The skills it may cast, tried in this order at a decision.</summary>
    public IReadOnlyList<MonsterSkill> Skills { get; }
}
}
