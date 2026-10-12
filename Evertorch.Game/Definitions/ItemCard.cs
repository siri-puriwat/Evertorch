using System;

namespace Evertorch.Game
{
/// <summary>
///     A card's facts (Gameplay Systems §11.7): the kind of item it fits, what it adds while that item is worn, and the
///     Smith's fee to take it out. The fit reaches the client; the rest is server-only content.
/// </summary>
public sealed class ItemCard
{
    public ItemCard(EquipmentSlot fits, CardBonus bonus, int removalFee)
    {
        if (fits != EquipmentSlot.Weapon && fits != EquipmentSlot.Armor)
        {
            throw new ArgumentOutOfRangeException(nameof(fits), fits, "A card fits a weapon or an armor.");
        }

        if (removalFee < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(removalFee), removalFee, "A fee cannot be negative.");
        }

        Fits = fits;
        Bonus = bonus ?? throw new ArgumentNullException(nameof(bonus));
        RemovalFee = removalFee;
    }

    public EquipmentSlot Fits { get; }

    public CardBonus Bonus { get; }

    public int RemovalFee { get; }
}
}
