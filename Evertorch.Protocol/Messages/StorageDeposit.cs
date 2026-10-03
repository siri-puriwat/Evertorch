using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Moves a quantity of the bag's row <see cref="InventoryItem" /> into the account's storage at the
///     Storekeeper <see cref="Npc" />, for its fee (Gameplay Systems §11.4). Answered after the commit.
/// </summary>
public sealed class StorageDeposit
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(long) + 2 * sizeof(uint);

    public StorageDeposit(EntityId npc, long inventoryItem, uint quantity, uint commandSequence)
    {
        if (inventoryItem <= 0 || quantity == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A row's ID and a quantity are at least 1.");
        }

        Npc = npc;
        InventoryItem = inventoryItem;
        Quantity = quantity;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public long InventoryItem { get; }

    public uint Quantity { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a row's ID or a quantity below 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out StorageDeposit? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.StorageDeposit)
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

        message = new StorageDeposit(new EntityId(npc), row, quantity, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.StorageDeposit);
        writer.WriteInt64(Npc.Value);
        writer.WriteInt64(InventoryItem);
        writer.WriteUInt32(Quantity);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
