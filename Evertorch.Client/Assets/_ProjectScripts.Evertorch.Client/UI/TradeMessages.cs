using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client's words for a trade (Prototype Content §2), composed from display names and the values the server
///     committed: a system line for a purchase or a sale, and the coins as the NPC window shows them.
/// </summary>
public static class TradeMessages
{
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
            string name = ItemName(content, moved.Key);
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

    private static string Units(string name, long count)
    {
        return count == 1 ? name : $"{name} x {count}";
    }
}
}
