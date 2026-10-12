using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     A card command the client asked for, kept by its command sequence so a refusal can say what was refused: the
///     card's bag row and its card, and the row it was to go into.
/// </summary>
public readonly struct CardRequest
{
    public CardRequest(long cardItem, ItemDefinitionId card, long inventoryItem)
    {
        CardItem = cardItem;
        Card = card;
        InventoryItem = inventoryItem;
    }

    public long CardItem { get; }

    public ItemDefinitionId Card { get; }

    public long InventoryItem { get; }
}
}
