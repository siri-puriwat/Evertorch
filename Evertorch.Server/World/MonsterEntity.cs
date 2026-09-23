using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     One live monster placed by a map's monster spawn. Its home is the spawn's center.
/// </summary>
public sealed class MonsterEntity : WorldEntity
{
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

    public override EntityKind Kind => EntityKind.Monster;

    public override string DefinitionId => Definition.Id.Value;
}
}
