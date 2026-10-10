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
    Trade = 10,

    /// <summary>
    ///     A deposit into the account's storage at the Storekeeper, for its fee (Gameplay Systems §11.4).
    /// </summary>
    StorageDeposit = 11,

    /// <summary>
    ///     A withdrawal from the account's storage at the Storekeeper (Gameplay Systems §11.4).
    /// </summary>
    StorageWithdraw = 12,

    /// <summary>
    ///     Units of a bag row put on the market board at the Broker, for its fee (Gameplay Systems §11.5).
    /// </summary>
    MarketList = 13,

    /// <summary>
    ///     Units bought on the market board from other accounts' listings (Gameplay Systems §11.5).
    /// </summary>
    MarketBuy = 14,

    /// <summary>
    ///     A listing's sales less the tax and its unsold units taken back, ending it (Gameplay Systems §11.5).
    /// </summary>
    MarketCollect = 15
}
}
