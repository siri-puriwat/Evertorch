using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What a press of an inventory row asks the server for (Prototype Content §2): a weapon or armor is put on, or
///     taken off when it is worn. Any other row asks for nothing.
/// </summary>
public static class InventoryActions
{
    public static bool HasAction(ItemType type)
    {
        return type == ItemType.Weapon || type == ItemType.Armor;
    }

    /// <summary>
    ///     Sends what a press of <paramref name="row" />, whose item is of <paramref name="type" />, asks for. Returns the
    ///     command's sequence, or 0 when it asks for nothing.
    /// </summary>
    public static uint Press(IItemCommandSink commands, InventoryEntry row, ItemType type)
    {
        if (!HasAction(type))
        {
            return 0;
        }

        return row.Slot == EquipmentSlot.None ? commands.SendEquip(row.InventoryItem) : commands.SendUnequip(row.Slot);
    }
}
}
