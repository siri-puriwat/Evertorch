using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A monster's death dropped an item. Sent right after the drop's <see cref="EntitySpawn" /> to the clients that
///     see it land; a client that meets the drop later gets the spawn alone and never learns the amount.
/// </summary>
public sealed class ItemDropped
{
    public ItemDropped(EntityId entity, string itemId, uint amount, WorldPosition position)
    {
        Entity = entity;
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
        Amount = amount;
        Position = position;
    }

    public EntityId Entity { get; }

    public string ItemId { get; }

    public uint Amount { get; }

    public WorldPosition Position { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out ItemDropped? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.ItemDropped)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemId)
            || !reader.TryReadUInt32(out uint amount)
            || !reader.TryReadPosition(out WorldPosition position)
            || !reader.IsAtEnd
            || entity == 0
            || amount == 0
            || !ItemDefinitionId.TryCreate(itemId, out ItemDefinitionId _))
        {
            return false;
        }

        message = new ItemDropped(new EntityId(entity), itemId, amount, position);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(long)
            + WireText.GetEncodedLength(ItemId, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint)
            + 3 * sizeof(float);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ItemDropped);
        writer.WriteInt64(Entity.Value);
        writer.WriteString(ItemId, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Amount);
        writer.WritePosition(Position);
        return writer.Position;
    }
}
}
