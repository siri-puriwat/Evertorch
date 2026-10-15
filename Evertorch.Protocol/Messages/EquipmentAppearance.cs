using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     What a player wears, as anyone who sees it may know (Network Protocol §6, §9): the item in each of the eight
///     positions, from the weapon to accessory 2, none where nothing is worn, and the revision of that appearance. It
///     follows each spawn of the player to every session but its owner's, and goes again when the player changes what
///     it wears; the owner learns that from its inventory. It names no row, refine level, or card.
/// </summary>
public sealed class EquipmentAppearance
{
    /// <summary>With nothing worn: the entity, the revision, and eight empty IDs.</summary>
    public const int MinEncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint)
        + PositionCount * WireText.LengthPrefixBytes;

    public const int MaxEncodedLength = MinEncodedLength + PositionCount * ProtocolLimits.MaxDefinitionIdBytes;

    private const int PositionCount = 8;

    private readonly ItemDefinitionId[] m_items;

    /// <param name="items">The item in each position, in position order; default where none.</param>
    public EquipmentAppearance(EntityId entity, uint revision, IReadOnlyList<ItemDefinitionId> items)
    {
        if (entity.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entity), "A player's entity ID is at least 1.");
        }

        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (items.Count != PositionCount)
        {
            throw new ArgumentException("An appearance names the eight positions.", nameof(items));
        }

        Entity = entity;
        Revision = revision;
        m_items = new ItemDefinitionId[PositionCount];
        for (int index = 0; index < PositionCount; index++)
        {
            m_items[index] = items[index];
        }
    }

    public EntityId Entity { get; }

    /// <summary>Raised at every change of what the player wears, so an inspection read at another is stale.</summary>
    public uint Revision { get; }

    /// <summary>The item in each position, in position order from the weapon; default where none.</summary>
    public IReadOnlyList<ItemDefinitionId> Items => m_items;

    /// <summary>The item worn in <paramref name="slot" />, one of the eight; default when none.</summary>
    public ItemDefinitionId ItemIn(EquipmentSlot slot)
    {
        if (!EquipmentPositions.IsPosition(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "One of the eight positions.");
        }

        return m_items[(int)slot - 1];
    }

    /// <summary>False for a malformed message, including an entity ID below 1 and an ID that is not an item's.</summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out EquipmentAppearance? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (source.Length > MaxEncodedLength
            || !reader.TryReadOpcode(MessageOpcode.EquipmentAppearance)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadUInt32(out uint revision)
            || entity <= 0)
        {
            return false;
        }

        var items = new ItemDefinitionId[PositionCount];
        for (int index = 0; index < PositionCount; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string text)
                || (text.Length > 0 && !ItemDefinitionId.TryCreate(text, out items[index])))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new EquipmentAppearance(new EntityId(entity), revision, items);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(long) + sizeof(uint);
        foreach (ItemDefinitionId item in m_items)
        {
            length += WireText.GetEncodedLength(item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EquipmentAppearance);
        writer.WriteInt64(Entity.Value);
        writer.WriteUInt32(Revision);
        foreach (ItemDefinitionId item in m_items)
        {
            writer.WriteString(item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        }

        return writer.Position;
    }
}
}
