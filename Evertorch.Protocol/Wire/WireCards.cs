using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A row's cards on the wire (Network Protocol §6): a count of at most <see cref="ProtocolLimits.MaxCardsPerItem" />,
///     then each card's item ID in slot order. Only a row of one holds cards.
/// </summary>
internal static class WireCards
{
    /// <summary>
    ///     The cards as a row keeps them: never null, at most four, and none on a row that is not of one.
    /// </summary>
    public static ItemDefinitionId[] Of(IReadOnlyList<ItemDefinitionId>? cards, uint quantity)
    {
        if (cards == null || cards.Count == 0)
        {
            return Array.Empty<ItemDefinitionId>();
        }

        if (cards.Count > ProtocolLimits.MaxCardsPerItem || quantity != 1)
        {
            throw new ArgumentException("A row of one holds at most four cards; no other row holds any.",
                nameof(cards));
        }

        var copy = new ItemDefinitionId[cards.Count];
        for (int index = 0; index < copy.Length; index++)
        {
            if (cards[index] == default)
            {
                throw new ArgumentException("A card has an item ID.", nameof(cards));
            }

            copy[index] = cards[index];
        }

        return copy;
    }

    public static int GetEncodedLength(IReadOnlyList<ItemDefinitionId> cards)
    {
        int length = sizeof(byte);
        foreach (ItemDefinitionId card in cards)
        {
            length += WireText.GetEncodedLength(card.Value, ProtocolLimits.MaxDefinitionIdBytes);
        }

        return length;
    }

    public static void Write(ref WireWriter writer, IReadOnlyList<ItemDefinitionId> cards)
    {
        writer.WriteByte((byte)cards.Count);
        foreach (ItemDefinitionId card in cards)
        {
            writer.WriteString(card.Value, ProtocolLimits.MaxDefinitionIdBytes);
        }
    }

    /// <summary>
    ///     False for more than four cards, an ID that breaks the rule, or any card on a row that is not of one.
    /// </summary>
    public static bool TryRead(ref WireReader reader, uint quantity, out ItemDefinitionId[] cards)
    {
        cards = Array.Empty<ItemDefinitionId>();
        if (!reader.TryReadByte(out byte count)
            || count > ProtocolLimits.MaxCardsPerItem
            || (count > 0 && quantity != 1))
        {
            return false;
        }

        if (count == 0)
        {
            return true;
        }

        var read = new ItemDefinitionId[count];
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string text)
                || !ItemDefinitionId.TryCreate(text, out read[index]))
            {
                return false;
            }
        }

        cards = read;
        return true;
    }
}
}
