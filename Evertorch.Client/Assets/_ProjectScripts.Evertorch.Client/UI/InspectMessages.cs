using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of an inspection (Prototype Content §2): the eight positions in order, each worn item named with its
///     refine level, its cards, and its slots, then each of its cards by name; "none" for an empty position.
/// </summary>
public static class InspectMessages
{
    /// <summary>
    ///     "Weapon: +7 Keen Iron Sword [2]", "· Forest Crawler Card", "Shield: none".
    /// </summary>
    public static IReadOnlyList<string> Lines(ClientContent? content, PlayerInspection shown)
    {
        var lines = new List<string>();
        foreach (EquipmentSlot slot in EquipmentPositions.All)
        {
            Add(lines, content, EquipmentMessages.PositionName(slot), shown.ItemIn(slot));
        }

        return lines;
    }

    private static void Add(List<string> lines, ClientContent? content, string position, InspectedItem item)
    {
        if (item.IsEmpty)
        {
            lines.Add($"{position}: none");
            return;
        }

        lines.Add($"{position}: {ShopMessages.RowName(content, item.Item, item.RefineLevel, item.Cards)}");
        foreach (ItemDefinitionId card in item.Cards)
        {
            lines.Add($"· {ShopMessages.ItemName(content, card)}");
        }
    }
}
}
