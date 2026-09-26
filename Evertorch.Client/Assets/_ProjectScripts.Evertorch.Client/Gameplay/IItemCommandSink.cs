using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Where the inventory's item actions go; the server checks and commits them (Gameplay Systems §11.1).
/// </summary>
public interface IItemCommandSink
{
    /// <summary>
    ///     Returns the command sequence the request carried; 0 when nothing was sent.
    /// </summary>
    uint SendEquip(long inventoryItem);

    /// <summary>
    ///     Returns the command sequence the request carried; 0 when nothing was sent.
    /// </summary>
    uint SendUnequip(EquipmentSlot slot);
}
}
