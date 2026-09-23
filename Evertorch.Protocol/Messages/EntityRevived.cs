using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A dead entity came back at a new place. Sent before visibility is recomputed to every client that knows it; the
///     move is a teleport, never interpolated.
/// </summary>
public readonly struct EntityRevived
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + 5 * sizeof(float) + sizeof(uint);

    public EntityRevived(EntityId entity, WorldPosition position, WorldDirection facing, uint serverTick)
    {
        Entity = entity;
        Position = position;
        Facing = facing;
        ServerTick = serverTick;
    }

    public EntityId Entity { get; }

    public WorldPosition Position { get; }

    public WorldDirection Facing { get; }

    public uint ServerTick { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out EntityRevived message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EntityRevived)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadPosition(out WorldPosition position)
            || !reader.TryReadDirection(out WorldDirection facing)
            || !reader.TryReadUInt32(out uint serverTick)
            || !reader.IsAtEnd
            || entity == 0)
        {
            return false;
        }

        message = new EntityRevived(new EntityId(entity), position, facing, serverTick);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EntityRevived);
        writer.WriteInt64(Entity.Value);
        writer.WritePosition(Position);
        writer.WriteDirection(Facing);
        writer.WriteUInt32(ServerTick);
        return writer.Position;
    }
}
}
