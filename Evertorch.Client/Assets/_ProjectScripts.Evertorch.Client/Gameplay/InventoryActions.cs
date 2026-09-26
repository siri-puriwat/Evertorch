using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What a press of an inventory row asks the server for (Prototype Content §2): a weapon or armor is put on, or
///     taken off when it is worn, and a consumable is used. Any other row asks for nothing.
/// </summary>
public static class InventoryActions
{
    public static bool HasAction(ItemType type)
    {
        return type == ItemType.Weapon || type == ItemType.Armor || type == ItemType.Consumable;
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

        if (!HasAction(type))
        {
            return 0;
        }

        return row.Slot == EquipmentSlot.None ? commands.SendEquip(row.InventoryItem) : commands.SendUnequip(row.Slot);
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
