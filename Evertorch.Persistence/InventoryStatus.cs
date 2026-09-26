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
    ///     Nothing changed: the row is not the character's, is already in the slot, or the slot is empty.
    /// </summary>
    Refused = 4
}
}
