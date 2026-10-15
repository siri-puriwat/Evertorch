using System;

namespace Evertorch.Game
{
/// <summary>
///     A card's facts (Gameplay Systems §11.7): the category of item it fits, what it adds while that item is worn,
///     and the Smith's fee to take it out. The fit reaches the client; the rest is server-only content.
/// </summary>
public sealed class ItemCard
{
    public ItemCard(ItemType fits, CardBonus bonus, int removalFee)
    {
        if (!EquipmentPositions.IsEquipment(fits))
        {
            throw new ArgumentOutOfRangeException(nameof(fits), fits, "A card fits a category of equipment.");
        }

        if (removalFee < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(removalFee), removalFee, "A fee cannot be negative.");
        }

        Fits = fits;
        Bonus = bonus ?? throw new ArgumentNullException(nameof(bonus));
        RemovalFee = removalFee;
    }

    /// <summary>The category of equipment the card enters.</summary>
    public ItemType Fits { get; }

    public CardBonus Bonus { get; }

    public int RemovalFee { get; }
}
}
