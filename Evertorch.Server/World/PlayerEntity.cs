using Evertorch.Game;
using Evertorch.Protocol;

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
        float movementSpeed)
        : base(id, position, facing, movementSpeed)
    {
        Character = character;
        Owner = owner;
        Job = job;
    }

    public CharacterId Character { get; }

    public ConnectionId Owner { get; }

    public JobDefinitionId Job { get; }

    public override EntityKind Kind => EntityKind.Player;

    public override string DefinitionId => Job.Value;
}
}
