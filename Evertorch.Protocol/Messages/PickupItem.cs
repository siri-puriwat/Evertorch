using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to pick up an item drop (Gameplay Systems §11). The client walks into range first; the server
///     checks everything again and answers with <see cref="ItemPickedUp" /> or <see cref="CommandRejected" />.
/// </summary>
public readonly struct PickupItem
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint);

    public PickupItem(EntityId drop, uint commandSequence)
    {
        Drop = drop;
        CommandSequence = commandSequence;
    }

    public EntityId Drop { get; }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out PickupItem message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PickupItem)
            || !reader.TryReadInt64(out long drop)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new PickupItem(new EntityId(drop), sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PickupItem);
        writer.WriteInt64(Drop.Value);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
