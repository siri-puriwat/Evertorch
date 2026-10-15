using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What a press of an inventory row asks the server for (Prototype Content §2): a piece of equipment is put on in its
///     category's position, or taken off when it is worn, and a consumable is used. A card's press opens the Card
///     window, and an unworn accessory's the Equipment window to choose its position, which the interface does, so they
///     ask the server for nothing here; any other row asks for nothing either.
/// </summary>
public static class InventoryActions
{
    public static bool HasAction(ItemType type)
    {
        return EquipmentPositions.IsEquipment(type) || type == ItemType.Consumable || type == ItemType.Card;
    }

    /// <summary>
    ///     Whether a press of <paramref name="row" /> opens the Equipment window with the row chosen: an unworn accessory,
    ///     which either accessory position takes (Gameplay Systems §11.1).
    /// </summary>
    public static bool ChoosesPosition(InventoryEntry row, ItemType type)
    {
        return type == ItemType.Accessory && row.Slot == EquipmentSlot.None;
    }

    /// <summary>
    ///     Sends what a press of <paramref name="row" />, whose item is of <paramref name="type" />, asks for. Returns the
    ///     command's sequence, or 0 when it asks for nothing.
    /// </summary>
    public static uint Press(IItemCommandSink commands, InventoryEntry row, ItemType type)
    {
        if (type == ItemType.Consumable)
        {
            return commands.SendUseItem(row.InventoryItem);
        }

        if (!EquipmentPositions.IsEquipment(type) || ChoosesPosition(row, type))
        {
            return 0;
        }

        return row.Slot == EquipmentSlot.None
            ? commands.SendEquip(row.InventoryItem, EquipmentPositions.Of(type)[0])
            : commands.SendUnequip(row.Slot);
    }

    /// <summary>
    ///     The row a potion slot drinks from (Prototype Content §4): the lowest-ID row of <paramref name="item" />.
    /// </summary>
    public static bool TryFindRow(IReadOnlyList<InventoryEntry> rows, ItemDefinitionId item, out InventoryEntry row)
    {
        row = default;
        bool isFound = false;
        foreach (InventoryEntry candidate in rows)
        {
            if (candidate.Item == item && (!isFound || candidate.InventoryItem < row.InventoryItem))
            {
                row = candidate;
                isFound = true;
            }
        }

        return isFound;
    }

    /// <summary>
    ///     How many units of <paramref name="item" /> the rows hold.
    /// </summary>
    public static int CountOf(IReadOnlyList<InventoryEntry> rows, ItemDefinitionId item)
    {
        int count = 0;
        foreach (InventoryEntry row in rows)
        {
            if (row.Item == item)
            {
                count += (int)row.Quantity;
            }
        }

        return count;
    }
}
}
