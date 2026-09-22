using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One entity's transform inside an <see cref="EntitySnapshot" />. Velocity is what the server simulated this tick,
///     so a receiver can tell standing from moving without differencing positions.
/// </summary>
public readonly struct EntityState
{
    public const int EncodedLength = sizeof(long)
        + 3 * sizeof(float)
        + 2 * sizeof(float)
        + 3 * sizeof(float)
        + sizeof(ushort);

    public EntityState(
        EntityId entity,
        WorldPosition position,
        WorldDirection facing,
        float velocityX,
        float velocityY,
        float velocityZ,
        EntityStateFlags stateFlags)
    {
        Entity = entity;
        Position = position;
        Facing = facing;
        VelocityX = velocityX;
        VelocityY = velocityY;
        VelocityZ = velocityZ;
        StateFlags = stateFlags;
    }

    public EntityId Entity { get; }

    public WorldPosition Position { get; }

    public WorldDirection Facing { get; }

    public float VelocityX { get; }

    public float VelocityY { get; }

    public float VelocityZ { get; }

    public EntityStateFlags StateFlags { get; }

    internal static bool TryRead(ref WireReader reader, out EntityState state)
    {
        state = default;
        if (!reader.TryReadInt64(out long entity)
            || !reader.TryReadPosition(out WorldPosition position)
            || !reader.TryReadDirection(out WorldDirection facing)
            || !reader.TryReadSingle(out float velocityX)
            || !reader.TryReadSingle(out float velocityY)
            || !reader.TryReadSingle(out float velocityZ)
            || !reader.TryReadUInt16(out ushort flags)
            || !WireEnums.IsDefined((EntityStateFlags)flags))
        {
            return false;
        }

        state = new EntityState(
            new EntityId(entity),
            position,
            facing,
            velocityX,
            velocityY,
            velocityZ,
            (EntityStateFlags)flags);
        return true;
    }

    internal void Write(ref WireWriter writer)
    {
        writer.WriteInt64(Entity.Value);
        writer.WritePosition(Position);
        writer.WriteDirection(Facing);
        writer.WriteSingle(VelocityX);
        writer.WriteSingle(VelocityY);
        writer.WriteSingle(VelocityZ);
        writer.WriteUInt16((ushort)StateFlags);
    }
}
}
