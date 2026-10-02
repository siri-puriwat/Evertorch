using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     One live monster placed by a map's monster spawn. Its home is the spawn's center.
/// </summary>
public sealed class MonsterEntity : WorldEntity
{
    private readonly List<DamageLogEntry> m_damageLog = new();

    public MonsterEntity(
        EntityId id,
        MonsterDefinition definition,
        MonsterSpawn spawn,
        WorldPosition position,
        WorldDirection facing,
        float movementSpeed)
        : base(id, position, facing, movementSpeed, definition.Hp, (float)definition.AttackRange)
    {
        Definition = definition;
        Spawn = spawn;
    }

    public MonsterDefinition Definition { get; }

    public MonsterSpawn Spawn { get; }

    public WorldPosition Home => Spawn.Center;

    public MonsterBrain Brain { get; } = new();

    /// <summary>
    ///     One entry per character that damaged the monster, in the order they first did (Gameplay Systems §2.1).
    /// </summary>
    public IReadOnlyList<DamageLogEntry> DamageLog => m_damageLog;

    public override EntityKind Kind => EntityKind.Monster;

    public override string DefinitionId => Definition.Id.Value;

    /// <summary>
    ///     Forgets every character that damaged the monster, as a boss does once home from its leash (Gameplay Systems
    ///     §10).
    /// </summary>
    public void ClearDamageLog()
    {
        m_damageLog.Clear();
    }

    public void LogDamage(CharacterId character, int damage)
    {
        for (int index = 0; index < m_damageLog.Count; index++)
        {
            if (m_damageLog[index].Character == character)
            {
                m_damageLog[index] = new DamageLogEntry(character, m_damageLog[index].Damage + damage);
                return;
            }
        }

        m_damageLog.Add(new DamageLogEntry(character, damage));
    }
}
}
