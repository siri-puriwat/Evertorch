using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A worn item as an inspection shows it (Network Protocol §6): its item, its refine level, and its cards in slot
///     order. An empty slot has no item, no level, and no card.
/// </summary>
public readonly struct InspectedItem
{
    private readonly ItemDefinitionId[]? m_cards;

    public InspectedItem(ItemDefinitionId item, byte refineLevel = 0, IReadOnlyList<ItemDefinitionId>? cards = null)
    {
        if (item == default && (refineLevel > 0 || (cards != null && cards.Count > 0)))
        {
            throw new ArgumentException("An empty slot has no level and no card.", nameof(item));
        }

        Item = item;
        RefineLevel = refineLevel;
        m_cards = WireCards.Of(cards, 1);
    }

    /// <summary>The item worn; default for an empty slot.</summary>
    public ItemDefinitionId Item { get; }

    public bool IsEmpty => Item == default;

    public byte RefineLevel { get; }

    /// <summary>The item's cards in slot order; empty, never null, on an item without any and on an empty slot.</summary>
    public IReadOnlyList<ItemDefinitionId> Cards => m_cards ?? Array.Empty<ItemDefinitionId>();

    internal int GetEncodedLength()
    {
        return WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(byte)
            + WireCards.GetEncodedLength(Cards);
    }

    internal void Write(ref WireWriter writer)
    {
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteByte(RefineLevel);
        WireCards.Write(ref writer, Cards);
    }

    /// <summary>
    ///     False for an ID that breaks the rule, more than four cards, and a level or a card on an empty slot.
    /// </summary>
    internal static bool TryRead(ref WireReader reader, out InspectedItem item)
    {
        item = default;
        if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadByte(out byte refineLevel)
            || !WireCards.TryRead(ref reader, 1, out ItemDefinitionId[] cards))
        {
            return false;
        }

        if (itemText.Length == 0)
        {
            return refineLevel == 0 && cards.Length == 0;
        }

        if (!ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId worn))
        {
            return false;
        }

        item = new InspectedItem(worn, refineLevel, cards);
        return true;
    }
}
}
