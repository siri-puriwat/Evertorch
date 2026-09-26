using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks to buy a quantity of an item the NPC sells (Gameplay Systems §6.1, §11.3). The server runs every check and
///     answers with <see cref="InventoryChanged" /> once the commit returns, or with <see cref="CommandRejected" />.
/// </summary>
public sealed class BuyItem
{
    public BuyItem(EntityId npc, ItemDefinitionId item, uint quantity, uint commandSequence)
    {
        if (item == default)
        {
            throw new ArgumentException("An item is required.", nameof(item));
        }

        if (quantity == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "A purchase is of at least one.");
        }

        Npc = npc;
        Item = item;
        Quantity = quantity;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public ItemDefinitionId Item { get; }

    public uint Quantity { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message: an NPC entity of 0 or below, a string that is not an item ID, or a quantity
    ///     of 0.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out BuyItem? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.BuyItem)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || npc <= 0
            || quantity == 0
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        message = new BuyItem(new EntityId(npc), item, quantity, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(long)
            + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + 2 * sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.BuyItem);
        writer.WriteInt64(Npc.Value);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Quantity);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
