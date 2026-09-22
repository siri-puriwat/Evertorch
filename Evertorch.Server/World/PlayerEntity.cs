using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     A player's authoritative presence on one map. Only the tick thread reads or writes it.
/// </summary>
public sealed class PlayerEntity
{
    public PlayerEntity(
        EntityId id,
        CharacterId character,
        ConnectionId owner,
        JobDefinitionId job,
        WorldPosition position,
        WorldDirection facing,
        float movementSpeed)
    {
        Id = id;
        Character = character;
        Owner = owner;
        Job = job;
        Position = position;
        Facing = facing;
        MovementSpeed = movementSpeed;
    }

    public EntityId Id { get; }

    public CharacterId Character { get; }

    public ConnectionId Owner { get; }

    public JobDefinitionId Job { get; }

    public WorldPosition Position { get; set; }

    public WorldDirection Facing { get; set; }

    public float VelocityX { get; set; }

    public float VelocityY { get; set; }

    public float VelocityZ { get; set; }

    public EntityStateFlags StateFlags { get; set; }

    /// <summary>
    ///     World units per second after the movement rules were applied; never a value the client supplied.
    /// </summary>
    public float MovementSpeed { get; }
}
}
