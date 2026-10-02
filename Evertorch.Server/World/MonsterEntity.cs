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
    private readonly List<MvpLogEntry> m_mvpLog = new();

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

    /// <summary>
    ///     A boss's second log, one entry per character it fought, in the order they first met (Gameplay Systems §10);
    ///     empty for any other monster.
    /// </summary>
    public IReadOnlyList<MvpLogEntry> MvpLog => m_mvpLog;

    public override EntityKind Kind => EntityKind.Monster;

    public override string DefinitionId => Definition.Id.Value;

    /// <summary>
    ///     Forgets every character that damaged the monster or that a boss fought, as a boss does once home from its
    ///     leash (Gameplay Systems §10).
    /// </summary>
    public void ClearDamageLog()
    {
        m_damageLog.Clear();
        m_mvpLog.Clear();
    }

    public void LogMvpDealt(CharacterId character, int damage)
    {
        LogMvp(character, damage, 0);
    }

    public void LogMvpTaken(CharacterId character, int damage)
    {
        LogMvp(character, 0, damage);
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

    private void LogMvp(CharacterId character, int dealt, int taken)
    {
        for (int index = 0; index < m_mvpLog.Count; index++)
        {
            MvpLogEntry entry = m_mvpLog[index];
            if (entry.Character == character)
            {
                m_mvpLog[index] = new MvpLogEntry(character, entry.Dealt + dealt, entry.Taken + taken);
                return;
            }
        }

        m_mvpLog.Add(new MvpLogEntry(character, dealt, taken));
    }
}
}
