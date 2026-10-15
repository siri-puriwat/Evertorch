namespace Evertorch.Persistence
{
/// <summary>
///     One equipment position of a character, by its stored name (Persistence §4): the weapon, body armor, which keeps
///     the name <c>Armor</c>, a headgear, a shield, a garment, footwear, and two accessories.
/// </summary>
internal sealed class EquipmentRow
{
    public const string WeaponSlot = "Weapon";
    public const string ArmorSlot = "Armor";
    public const string HeadgearSlot = "Headgear";
    public const string ShieldSlot = "Shield";
    public const string GarmentSlot = "Garment";
    public const string FootwearSlot = "Footwear";
    public const string FirstAccessorySlot = "Accessory1";
    public const string SecondAccessorySlot = "Accessory2";

    public long CharacterId { get; set; }

    public string Slot { get; set; } = string.Empty;

    public long InventoryItemId { get; set; }

    public int Version { get; set; }
}
}
