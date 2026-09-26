namespace Evertorch.Server
{
/// <summary>
///     What an inventory operation does; its ledger row carries the same type (Persistence §4).
/// </summary>
public enum InventoryOperationKind
{
    Pickup = 1,
    Equip = 2,
    Unequip = 3,
    Consume = 4
}
}
