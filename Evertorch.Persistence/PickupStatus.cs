namespace Evertorch.Persistence
{
public enum PickupStatus
{
    /// <summary>
    ///     The character holds the item: committed now, or found already committed for it.
    /// </summary>
    Committed = 1,

    /// <summary>
    ///     Nothing changed: the pickup would exceed the stack limit or need one row too many.
    /// </summary>
    InventoryFull = 2,

    /// <summary>
    ///     Nothing changed: the drop was already committed to another character.
    /// </summary>
    TakenByOther = 3
}
}
