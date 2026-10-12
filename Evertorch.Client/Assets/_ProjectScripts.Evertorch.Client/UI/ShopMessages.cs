using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client's words for a purchase or a sale (Prototype Content §2), composed from display names and the values the
///     server
///     committed: a system line for a purchase or a sale, and the coins as the NPC window shows them.
/// </summary>
public static class ShopMessages
{
    private static readonly string[] Repeats = { string.Empty, "Double ", "Triple ", "Quadruple " };

    /// <summary>
    ///     The system line for a committed change that bought or sold one kind of item, told apart by which way the
    ///     coins and the units moved; null for any other change.
    /// </summary>
    public static string? Describe(InventoryDelta delta, ClientContent? content)
    {
        if (delta.Units.Count != 1)
        {
            return null;
        }

        foreach (KeyValuePair<ItemDefinitionId, long> moved in delta.Units)
        {
            string name = RowName(content, moved.Key, delta.RefineLevel, delta.Cards);
            if (moved.Value > 0 && delta.CoinsAfter < delta.CoinsBefore)
            {
                return $"Bought {Units(name, moved.Value)} for {Coins(delta.CoinsBefore - delta.CoinsAfter)}.";
            }

            if (moved.Value < 0 && delta.CoinsAfter > delta.CoinsBefore)
            {
                return $"Sold {Units(name, -moved.Value)} for {Coins(delta.CoinsAfter - delta.CoinsBefore)}.";
            }
        }

        return null;
    }

    public static string Coins(long coins)
    {
        return coins == 1 ? "1 coin" : $"{coins} coins";
    }

    public static string ItemName(ClientContent? content, ItemDefinitionId item)
    {
        return content != null && content.TryGetItem(item, out ClientItem? found) && found != null
            ? found.DisplayName
            : item.Value;
    }

    /// <summary>
    ///     "+7 Iron Sword" above +0; at +0 the name as it is (Prototype Content §2).
    /// </summary>
    public static string ItemName(ClientContent? content, ItemDefinitionId item, byte refineLevel)
    {
        return Refined(ItemName(content, item), refineLevel);
    }

    public static string Refined(string name, byte refineLevel)
    {
        return refineLevel > 0 ? $"+{refineLevel} {name}" : name;
    }

    /// <summary>
    ///     A row's name (Prototype Content §2): its refine level above +0, its cards' prefixes in slot order with a repeat
    ///     as "Double", "Triple", or "Quadruple", the item's name, and its slots when it has any: "+7 Keen Bane Iron Sword
    ///     [2]".
    /// </summary>
    public static string RowName(
        ClientContent? content,
        ItemDefinitionId item,
        byte refineLevel,
        IReadOnlyList<ItemDefinitionId> cards)
    {
        if (cards == null)
        {
            throw new ArgumentNullException(nameof(cards));
        }

        var name = new StringBuilder();
        string prefixes = Prefixes(content, cards);
        if (prefixes.Length > 0)
        {
            name.Append(prefixes).Append(' ');
        }

        name.Append(ItemName(content, item));
        if (content != null && content.TryGetItem(item, out ClientItem? found) && found != null && found.Slots > 0)
        {
            name.Append(" [").Append(found.Slots).Append(']');
        }

        return Refined(name.ToString(), refineLevel);
    }

    /// <summary>
    ///     "Keen Bane", or "Double Keen" for two of one card, in the order the cards first appear; a card the content
    ///     lacks is named by its ID.
    /// </summary>
    public static string Prefixes(ClientContent? content, IReadOnlyList<ItemDefinitionId> cards)
    {
        var order = new List<string>(cards.Count);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ItemDefinitionId card in cards)
        {
            string prefix = content != null
                && content.TryGetItem(card, out ClientItem? found)
                && found != null
                && found.Prefix.Length > 0
                    ? found.Prefix
                    : card.Value;
            if (!counts.ContainsKey(prefix))
            {
                order.Add(prefix);
                counts[prefix] = 0;
            }

            counts[prefix]++;
        }

        var words = new List<string>(order.Count);
        foreach (string prefix in order)
        {
            words.Add(Repeats[Math.Min(counts[prefix], Repeats.Length) - 1] + prefix);
        }

        return string.Join(" ", words);
    }

    /// <summary>
    ///     "Press again to sell +7 Iron Sword for 25 coins": a refined row sells at its item's price, whatever its
    ///     level, so its sale asks twice; a carded row's asks twice too and says its cards are lost (Prototype Content
    ///     §2).
    /// </summary>
    public static string SellPrompt(
        ClientContent? content,
        ItemDefinitionId item,
        byte refineLevel,
        uint price,
        IReadOnlyList<ItemDefinitionId> cards)
    {
        return cards.Count > 0
            ? "Press again to sell. Its cards are lost."
            : $"Press again to sell {RowName(content, item, refineLevel, cards)} for {Coins(price)}";
    }

    private static string Units(string name, long count)
    {
        return count == 1 ? name : $"{name} x {count}";
    }
}
}
