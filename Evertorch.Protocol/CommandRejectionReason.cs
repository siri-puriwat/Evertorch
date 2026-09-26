namespace Evertorch.Protocol
{
/// <summary>
///     Why a sequenced command was refused (Network Protocol §11). Zero is never sent.
/// </summary>
public enum CommandRejectionReason : byte
{
    None = 0,

    /// <summary>
    ///     The target is missing, hidden, dead, untargetable, or already picked up. These look the same, so a refusal
    ///     reveals nothing hidden.
    /// </summary>
    InvalidTarget = 1,

    OutOfRange = 2,

    /// <summary>
    ///     The character's state forbids it now: dead, alive for a respawn, or already logging out.
    /// </summary>
    NotAllowedNow = 3,

    /// <summary>
    ///     Another character still has the first right to the drop.
    /// </summary>
    LootPriority = 4,

    /// <summary>
    ///     No inventory row is free, or the stack would pass the item's limit.
    /// </summary>
    InventoryFull = 5,

    /// <summary>
    ///     The database is unavailable. Trying again later may work.
    /// </summary>
    ServiceUnavailable = 6,

    /// <summary>
    ///     The drop is reserved by a pickup still being committed. Trying again later may work.
    /// </summary>
    Busy = 7,

    /// <summary>
    ///     The character has less SP than the skill costs.
    /// </summary>
    NotEnoughSp = 8,

    /// <summary>
    ///     Another item action of the character is still being committed: a use, an equip, or an unequip, or a pickup
    ///     while one of those is. Trying again later may work.
    /// </summary>
    ItemActionInFlight = 9,

    /// <summary>
    ///     The character has fewer coins than the purchase costs.
    /// </summary>
    NotEnoughCoins = 10,

    /// <summary>
    ///     The coins would pass their cap; a sale or a reward is refused rather than cut (Gameplay Systems §11.3).
    /// </summary>
    CoinCapReached = 11
}
}
