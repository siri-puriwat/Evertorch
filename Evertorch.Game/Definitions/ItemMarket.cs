using System;

namespace Evertorch.Game
{
/// <summary>
///     An item's terms on the market board (Gameplay Systems §11.5; Content Pipeline §4): the price the board starts it
///     at, the floor its price never falls below, and the ceiling it never rises past. Server-only content.
/// </summary>
public sealed class ItemMarket
{
    public ItemMarket(int basePrice, int floor, int ceiling)
    {
        if (floor < 1 || basePrice < floor || ceiling < basePrice)
        {
            throw new ArgumentOutOfRangeException(
                nameof(basePrice),
                "The floor is at least 1, the base at least the floor, and the ceiling at least the base.");
        }

        BasePrice = basePrice;
        Floor = floor;
        Ceiling = ceiling;
    }

    public int BasePrice { get; }

    public int Floor { get; }

    public int Ceiling { get; }
}
}
