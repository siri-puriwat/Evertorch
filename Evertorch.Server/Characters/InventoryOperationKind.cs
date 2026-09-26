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
    Consume = 4,
    Buy = 5,
    Sell = 6,

    /// <summary>
    ///     A quest's turn-in, which pays the reward's coins with the inventory's revision.
    /// </summary>
    QuestReward = 7
}
}
