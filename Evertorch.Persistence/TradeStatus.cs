namespace Evertorch.Persistence
{
/// <summary>
///     How a trade's commit ended (Persistence §5).
/// </summary>
public enum TradeStatus
{
    /// <summary>
    ///     Both inventories changed: committed now, or found committed already.
    /// </summary>
    Committed = 1,

    /// <summary>
    ///     Nothing moved: a receiver's bag would need a row too many or pass a stack limit.
    /// </summary>
    InventoryFull = 2,

    /// <summary>
    ///     Nothing moved: a receiver's coins would pass their cap.
    /// </summary>
    CoinCapReached = 3,

    /// <summary>
    ///     Nothing moved: a giver no longer holds a row, its quantity, or its coins, or wears the row.
    /// </summary>
    Refused = 4
}
}
