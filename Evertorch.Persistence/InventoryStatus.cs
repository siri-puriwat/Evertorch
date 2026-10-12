namespace Evertorch.Persistence
{
public enum InventoryStatus
{
    /// <summary>
    ///     The character's inventory changed: committed now, or found already committed for it.
    /// </summary>
    Committed = 1,

    /// <summary>
    ///     Nothing changed: a pickup would exceed the stack limit or need one row too many.
    /// </summary>
    InventoryFull = 2,

    /// <summary>
    ///     Nothing changed: the operation was already committed to another character.
    /// </summary>
    TakenByOther = 3,

    /// <summary>
    ///     Nothing changed: the row is not the character's, is already in the slot, or the slot is empty; a consumed
    ///     row is not the character's; or a row to refine is gone or at another level.
    /// </summary>
    Refused = 4,

    /// <summary>
    ///     Nothing changed: the board is not as the command saw it. Its price passed the buyer's limit, too few units are
    ///     left, or the listing is gone or another character's (Gameplay Systems §11.5).
    /// </summary>
    BoardChanged = 5,

    /// <summary>
    ///     Nothing changed: the coins fall short of a listing's fee, a purchase's price, or a refine's cost.
    /// </summary>
    NotEnoughCoins = 6,

    /// <summary>
    ///     Nothing changed: a collection's coins would pass the cap.
    /// </summary>
    CoinCapReached = 7,

    /// <summary>
    ///     Nothing changed: the character already holds its most listings.
    /// </summary>
    ListingCapReached = 8,

    /// <summary>
    ///     Nothing changed: a refine's ore is not in its row, or too few of it (Gameplay Systems §11.6).
    /// </summary>
    MaterialShort = 9,

    /// <summary>
    ///     Nothing changed: the row to refine, or to put a card into or take one out of, is worn.
    /// </summary>
    RowWorn = 10,

    /// <summary>
    ///     Nothing changed: the item holds as many cards as its slots (Gameplay Systems §11.7).
    /// </summary>
    NoFreeSlot = 11
}
}
