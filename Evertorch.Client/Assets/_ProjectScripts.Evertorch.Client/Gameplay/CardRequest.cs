using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     A card command the client asked for, kept by its command sequence so a refusal can say what was refused: an
///     insertion's card row, its card, and the row it was to go into, or a removal's card, its row, and its slot.
/// </summary>
public readonly struct CardRequest
{
    public CardRequest(long cardItem, ItemDefinitionId card, long inventoryItem, byte slot = 0)
    {
        CardItem = cardItem;
        Card = card;
        InventoryItem = inventoryItem;
        Slot = slot;
    }

    /// <summary>The card's bag row of an insertion; 0 for a removal.</summary>
    public long CardItem { get; }

    public ItemDefinitionId Card { get; }

    public long InventoryItem { get; }

    /// <summary>The slot a removal takes the card from, from 1; 0 for an insertion.</summary>
    public byte Slot { get; }

    public bool IsRemoval => Slot > 0;
}
}
