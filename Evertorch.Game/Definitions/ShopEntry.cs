namespace Evertorch.Game
{
/// <summary>
///     One item an NPC's shop sells and its price in coins (Gameplay Systems §11.3).
/// </summary>
public sealed class ShopEntry
{
    public ShopEntry(ItemDefinitionId item, int price)
    {
        Item = item;
        Price = price;
    }

    public ItemDefinitionId Item { get; }

    public int Price { get; }
}
}
