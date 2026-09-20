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
        double baseSpeed,
        double attackRange,
        int attackIntervalMs,
        MonsterBehavior behavior,
        double perceptionRadius,
        double leashRadius,
        IReadOnlyList<MonsterDrop> drops)
    {
        Id = id;
        DisplayName = displayName;
        Level = level;
        Hp = hp;
        PhysicalAttack = physicalAttack;
        PhysicalDefense = physicalDefense;
        Hit = hit;
        Flee = flee;
        BaseSpeed = baseSpeed;
        AttackRange = attackRange;
        AttackIntervalMs = attackIntervalMs;
        Behavior = behavior;
        PerceptionRadius = perceptionRadius;
        LeashRadius = leashRadius;
        Drops = drops;
    }

    public MonsterDefinitionId Id { get; }

    public string DisplayName { get; }

    public int Level { get; }

    public int Hp { get; }

    public int PhysicalAttack { get; }

    public int PhysicalDefense { get; }

    public int Hit { get; }

    public int Flee { get; }

    /// <summary>World units per second before any movement rule is applied.</summary>
    public double BaseSpeed { get; }

    public double AttackRange { get; }

    public int AttackIntervalMs { get; }

    public MonsterBehavior Behavior { get; }

    public double PerceptionRadius { get; }

    public double LeashRadius { get; }

    public IReadOnlyList<MonsterDrop> Drops { get; }
}
}
