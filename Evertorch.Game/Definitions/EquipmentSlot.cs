namespace Evertorch.Game
{
/// <summary>
///     Where an item is worn (Gameplay Systems §11.1); the values are the ones on the wire. Which items may go where is
///     <see cref="EquipmentPositions" />'s to say.
/// </summary>
public enum EquipmentSlot : byte
{
    None = 0,
    Weapon = 1,

    /// <summary>Body armor, whose stored name stays <c>Armor</c>.</summary>
    Armor = 2,

    Headgear = 3,
    Shield = 4,
    Garment = 5,
    Footwear = 6,
    Accessory1 = 7,
    Accessory2 = 8
}
}
