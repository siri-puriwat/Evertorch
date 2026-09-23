using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A pickup was committed (Network Protocol §9). Sent to every client that knows the drop, before the drop's
///     <see cref="EntityDespawn" />; the picker also gets <see cref="InventoryChanged" />.
/// </summary>
public sealed class ItemPickedUp
{
    public ItemPickedUp(EntityId drop, EntityId recipient, ItemDefinitionId item, uint amount)
    {
        Drop = drop;
        Recipient = recipient;
        Item = item;
        Amount = amount;
    }

    public EntityId Drop { get; }

    /// <summary>
    ///     The picker's entity, or 0 for a receiver that has no spawn for the picker.
    /// </summary>
    public EntityId Recipient { get; }

    public ItemDefinitionId Item { get; }

    public uint Amount { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out ItemPickedUp? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.ItemPickedUp)
            || !reader.TryReadInt64(out long drop)
            || !reader.TryReadInt64(out long recipient)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint amount)
            || !reader.IsAtEnd
            || drop == 0
            || amount == 0
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        message = new ItemPickedUp(new EntityId(drop), new EntityId(recipient), item, amount);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + 2 * sizeof(long)
            + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ItemPickedUp);
        writer.WriteInt64(Drop.Value);
        writer.WriteInt64(Recipient.Value);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Amount);
        return writer.Position;
    }
}
}
