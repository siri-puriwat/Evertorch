using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of an inspection (Prototype Content §2): the worn weapon and armor, each named with its refine level,
///     its cards, and its slots, then each of its cards by name; "none" for an empty slot.
/// </summary>
public static class InspectMessages
{
    /// <summary>
    ///     "Weapon: +7 Keen Iron Sword [2]", "· Forest Crawler Card", "Armor: none".
    /// </summary>
    public static IReadOnlyList<string> Lines(ClientContent? content, InspectedEquipment shown)
    {
        var lines = new List<string>();
        Add(lines, content, "Weapon", shown.Weapon);
        Add(lines, content, "Armor", shown.Armor);
        return lines;
    }

    private static void Add(List<string> lines, ClientContent? content, string slot, InspectedItem item)
    {
        if (item.IsEmpty)
        {
            lines.Add($"{slot}: none");
            return;
        }

        lines.Add($"{slot}: {ShopMessages.RowName(content, item.Item, item.RefineLevel, item.Cards)}");
        foreach (ItemDefinitionId card in item.Cards)
        {
            lines.Add($"· {ShopMessages.ItemName(content, card)}");
        }
    }
}
}
