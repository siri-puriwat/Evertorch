namespace Evertorch.Game
{
/// <summary>
///     What an item is; for a piece of equipment, its category, which decides where it is worn
///     (<see cref="EquipmentPositions" />). Content names each in camelCase; no number travels.
/// </summary>
public enum ItemType
{
    Material,
    Consumable,
    Weapon,
    Armor,
    Card,
    Headgear,
    Shield,
    Garment,
    Footwear,
    Accessory
}
}
