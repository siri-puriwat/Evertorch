using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     An entity's HP reached 0. The source is 0 when there is none or the receiver has no spawn for it.
/// </summary>
public readonly struct EntityDied
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(long) + sizeof(uint);

    public EntityDied(EntityId entity, EntityId source, uint serverTick)
    {
        Entity = entity;
        Source = source;
        ServerTick = serverTick;
    }

    public EntityId Entity { get; }

    public EntityId Source { get; }

    public uint ServerTick { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out EntityDied message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EntityDied)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadInt64(out long killer)
            || !reader.TryReadUInt32(out uint serverTick)
            || !reader.IsAtEnd
            || entity == 0)
        {
            return false;
        }

        message = new EntityDied(new EntityId(entity), new EntityId(killer), serverTick);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EntityDied);
        writer.WriteInt64(Entity.Value);
        writer.WriteInt64(Source.Value);
        writer.WriteUInt32(ServerTick);
        return writer.Position;
    }
}
}
