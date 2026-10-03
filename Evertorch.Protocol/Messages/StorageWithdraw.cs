using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Moves a quantity of the storage's row <see cref="StorageItem" /> into the bag at the Storekeeper
///     <see cref="Npc" /> (Gameplay Systems §11.4). Answered after the commit.
/// </summary>
public sealed class StorageWithdraw
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(long) + 2 * sizeof(uint);

    public StorageWithdraw(EntityId npc, long storageItem, uint quantity, uint commandSequence)
    {
        if (storageItem <= 0 || quantity == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A row's ID and a quantity are at least 1.");
        }

        Npc = npc;
        StorageItem = storageItem;
        Quantity = quantity;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public long StorageItem { get; }

    public uint Quantity { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a row's ID or a quantity below 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out StorageWithdraw? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.StorageWithdraw)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadInt64(out long row)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || row <= 0
            || quantity == 0)
        {
            return false;
        }

        message = new StorageWithdraw(new EntityId(npc), row, quantity, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.StorageWithdraw);
        writer.WriteInt64(Npc.Value);
        writer.WriteInt64(StorageItem);
        writer.WriteUInt32(Quantity);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
