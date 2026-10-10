namespace Evertorch.Persistence
{
/// <summary>
///     One item on the board as a character reads it: its price at the read's time and the units of other accounts'
///     unexpired listings, which the character may buy.
/// </summary>
public sealed class StoredMarketEntry
{
    public StoredMarketEntry(string itemDefinitionId, int price, long stock)
    {
        ItemDefinitionId = itemDefinitionId;
        Price = price;
        Stock = stock;
    }

    public string ItemDefinitionId { get; }

    public int Price { get; }

    public long Stock { get; }
}
}
