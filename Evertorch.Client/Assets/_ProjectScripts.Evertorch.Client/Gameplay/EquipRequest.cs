using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     An equip the client asked for, kept by its command sequence so a refusal can say what was refused: the bag's row
///     and the position it was to go to.
/// </summary>
public readonly struct EquipRequest
{
    public EquipRequest(long inventoryItem, EquipmentSlot destination)
    {
        InventoryItem = inventoryItem;
        Destination = destination;
    }

    public long InventoryItem { get; }

    public EquipmentSlot Destination { get; }
}
}
