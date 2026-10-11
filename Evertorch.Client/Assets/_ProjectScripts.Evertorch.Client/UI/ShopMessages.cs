using System.Collections.Generic;
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
            string name = ItemName(content, moved.Key, delta.RefineLevel);
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
    ///     "Press again to sell +7 Iron Sword for 25 coins": a refined row sells at its item's price, whatever its
    ///     level, so its sale asks twice.
    /// </summary>
    public static string SellPrompt(ClientContent? content, ItemDefinitionId item, byte refineLevel, uint price)
    {
        return $"Press again to sell {ItemName(content, item, refineLevel)} for {Coins(price)}";
    }

    private static string Units(string name, long count)
    {
        return count == 1 ? name : $"{name} x {count}";
    }
}
}
