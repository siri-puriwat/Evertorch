using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of the eight positions (Prototype Content §2): their names, what each holds in the Equipment window, the
///     rows a position may take, and an equip's refusals.
/// </summary>
public static class EquipmentMessages
{
    /// <summary>
    ///     "Weapon", "Body armor", "Headgear", "Shield", "Garment", "Footwear", "Accessory 1", or "Accessory 2".
    /// </summary>
    public static string PositionName(EquipmentSlot slot)
    {
        return slot switch
        {
            EquipmentSlot.Weapon => "Weapon",
            EquipmentSlot.Armor => "Body armor",
            EquipmentSlot.Headgear => "Headgear",
            EquipmentSlot.Shield => "Shield",
            EquipmentSlot.Garment => "Garment",
            EquipmentSlot.Footwear => "Footwear",
            EquipmentSlot.Accessory1 => "Accessory 1",
            EquipmentSlot.Accessory2 => "Accessory 2",
            _ => string.Empty
        };
    }

    /// <summary>
    ///     A position as the Equipment window names it: "Weapon: +7 Keen Iron Sword [2]", or "Shield: empty".
    /// </summary>
    public static string Position(ClientContent? content, EquipmentSlot slot, InventoryEntry? worn)
    {
        return worn.HasValue
            ? $"{PositionName(slot)}: {ShopMessages.RowName(content, worn.Value.Item, worn.Value.RefineLevel, worn.Value.Cards)}"
            : $"{PositionName(slot)}: empty";
    }

    /// <summary>
    ///     The line over the positions while an accessory is chosen: "Wear Traveler Charm in Accessory 1 or Accessory 2".
    /// </summary>
    public static string ChoosePosition(ClientContent? content, IReadOnlyList<InventoryEntry> rows, long chosenRow)
    {
        string name = string.Empty;
        foreach (InventoryEntry row in rows)
        {
            if (row.InventoryItem == chosenRow)
            {
                name = ShopMessages.RowName(content, row.Item, row.RefineLevel, row.Cards);
            }
        }

        return $"Wear {name} in {PositionName(EquipmentSlot.Accessory1)} or {PositionName(EquipmentSlot.Accessory2)}";
    }

    /// <summary>
    ///     The row worn in <paramref name="slot" />, if any.
    /// </summary>
    public static InventoryEntry? WornIn(IReadOnlyList<InventoryEntry> rows, EquipmentSlot slot)
    {
        foreach (InventoryEntry row in rows)
        {
            if (row.Slot == slot)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    ///     The unworn rows <paramref name="slot" /> may take, in row order: those whose item's category goes there, an
    ///     accessory's to either accessory position (Gameplay Systems §11.1).
    /// </summary>
    public static IReadOnlyList<InventoryEntry> ChoicesFor(
        ClientContent? content,
        IReadOnlyList<InventoryEntry> rows,
        EquipmentSlot slot)
    {
        var choices = new List<InventoryEntry>();
        if (content == null)
        {
            return choices;
        }

        foreach (InventoryEntry row in rows)
        {
            if (row.Slot == EquipmentSlot.None
                && content.TryGetItem(row.Item, out ClientItem? item)
                && item != null
                && EquipmentPositions.Fits(item.Type, slot))
            {
                choices.Add(row);
            }
        }

        return choices;
    }

    /// <summary>
    ///     The words for a refused <paramref name="request" /> (Prototype Content §2): the server refuses a row worn
    ///     anywhere and a position its category cannot go to alike, so the rows tell them apart, "Take it off first."
    ///     and "That doesn't go there."; any other refusal reads as an equip's.
    /// </summary>
    public static string DescribeRefusal(
        EquipRequest request,
        IReadOnlyList<InventoryEntry> rows,
        CommandRejectionReason reason,
        ClientContent? content)
    {
        if (reason == CommandRejectionReason.NotAllowedNow)
        {
            foreach (InventoryEntry row in rows)
            {
                if (row.InventoryItem != request.InventoryItem)
                {
                    continue;
                }

                if (row.Slot != EquipmentSlot.None)
                {
                    return "Take it off first.";
                }

                if (content != null
                    && content.TryGetItem(row.Item, out ClientItem? item)
                    && item != null
                    && !EquipmentPositions.Fits(item.Type, request.Destination))
                {
                    return "That doesn't go there.";
                }
            }
        }

        return RejectionMessages.Describe(reason, true);
    }
}
}
