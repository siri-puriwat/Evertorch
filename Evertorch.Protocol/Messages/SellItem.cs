using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks to sell a quantity of an inventory row to the NPC (Gameplay Systems §6.1, §11.3). The server runs every
///     check and answers with <see cref="InventoryChanged" /> once the commit returns, or with
///     <see cref="CommandRejected" />.
/// </summary>
public readonly struct SellItem
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(long) + 2 * sizeof(uint);

    public SellItem(EntityId npc, long inventoryItem, uint quantity, uint commandSequence)
    {
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
    ///     False for a malformed message: an NPC entity or a row ID of 0 or below, or a quantity of 0.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out SellItem message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.SellItem)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || npc <= 0
            || inventoryItem <= 0
            || quantity == 0)
        {
            return false;
        }

        message = new SellItem(new EntityId(npc), inventoryItem, quantity, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.SellItem);
        writer.WriteInt64(Npc.Value);
        writer.WriteInt64(InventoryItem);
        writer.WriteUInt32(Quantity);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
