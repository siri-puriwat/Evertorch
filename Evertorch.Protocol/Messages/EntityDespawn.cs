using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
public readonly struct EntityDespawn
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(byte);

    public EntityDespawn(EntityId entity, DespawnReason reason)
    {
        Entity = entity;
        Reason = reason;
    }

    public EntityId Entity { get; }

    public DespawnReason Reason { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out EntityDespawn message)
    {
        message = default;
        WireReader reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EntityDespawn)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadByte(out byte reasonValue)
            || !reader.IsAtEnd
            || !WireEnums.IsDefined((DespawnReason)reasonValue))
        {
            return false;
        }

        message = new EntityDespawn(new EntityId(entity), (DespawnReason)reasonValue);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        WireWriter writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EntityDespawn);
        writer.WriteInt64(Entity.Value);
        writer.WriteByte((byte)Reason);
        return writer.Position;
    }
}
}
