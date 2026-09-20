using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class MonsterDefinition
{
    public MonsterDefinition(
        DefinitionSource source,
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
        IReadOnlyList<MonsterDrop> drops,
        string prefab,
        string icon)
    {
        Source = source;
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
        Prefab = prefab;
        Icon = icon;
    }

    public DefinitionSource Source { get; }

    public MonsterDefinitionId Id { get; }

    public string DisplayName { get; }

    public int Level { get; }

    public int Hp { get; }

    public int PhysicalAttack { get; }

    public int PhysicalDefense { get; }

    public int Hit { get; }

    public int Flee { get; }

    public double BaseSpeed { get; }

    public double AttackRange { get; }

    public int AttackIntervalMs { get; }

    public MonsterBehavior Behavior { get; }

    public double PerceptionRadius { get; }

    public double LeashRadius { get; }

    public IReadOnlyList<MonsterDrop> Drops { get; }

    public string Prefab { get; }

    public string Icon { get; }
}
}
