using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Reads the account's storage at the Storekeeper <see cref="Npc" /> (Gameplay Systems §11.4), answered with the
///     storage's snapshot parts.
/// </summary>
public sealed class StorageOpen
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint);

    public StorageOpen(EntityId npc, uint commandSequence)
    {
        Npc = npc;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out StorageOpen? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.StorageOpen)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new StorageOpen(new EntityId(npc), sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.StorageOpen);
        writer.WriteInt64(Npc.Value);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
