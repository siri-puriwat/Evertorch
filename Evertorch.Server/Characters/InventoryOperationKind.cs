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
    QuestReward = 7,

    /// <summary>
    ///     A job change, which takes a weapon the new job cannot wield off with the job (Persistence §5).
    /// </summary>
    JobChange = 8,

    /// <summary>
    ///     A boss's prize for its most valuable player, which the server starts without a command (Gameplay Systems
    ///     §11).
    /// </summary>
    BossReward = 9,

    /// <summary>
    ///     A face-to-face trade, one operation of each trader under the trade's one ID, started by the server once both
    ///     confirmed (Gameplay Systems §16).
    /// </summary>
    Trade = 10
}
}
