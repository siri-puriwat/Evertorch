using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Anything with an authoritative presence on a map: its identity, transform, and state flags. Only the tick
///     thread reads or writes it.
/// </summary>
public abstract class WorldEntity
{
    protected WorldEntity(EntityId id, WorldPosition position, WorldDirection facing, float movementSpeed)
    {
        Id = id;
        Position = position;
        Facing = facing;
        MovementSpeed = movementSpeed;
    }

    public EntityId Id { get; }

    public abstract EntityKind Kind { get; }

    /// <summary>
    ///     The definition clients present this entity with; its kind follows <see cref="Kind" />.
    /// </summary>
    public abstract string DefinitionId { get; }

    public WorldPosition Position { get; set; }

    public WorldDirection Facing { get; set; }

    public float VelocityX { get; set; }

    public float VelocityY { get; set; }

    public float VelocityZ { get; set; }

    public EntityStateFlags StateFlags { get; set; }

    /// <summary>
    ///     The entity this one has selected; the default value means none.
    /// </summary>
    public EntityId Target { get; set; }

    /// <summary>
    ///     World units per second after the movement rules were applied; never a value a client supplied.
    /// </summary>
    public float MovementSpeed { get; }
}
}
