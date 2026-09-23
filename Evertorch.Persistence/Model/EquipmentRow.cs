namespace Evertorch.Persistence
{
/// <summary>
///     One equipment slot of a character. Nothing equips in Milestone 4; the table and its constraints exist so later
///     milestones add behaviour, not schema.
/// </summary>
internal sealed class EquipmentRow
{
    public const string WeaponSlot = "Weapon";
    public const string ArmorSlot = "Armor";

    public long CharacterId { get; set; }

    public string Slot { get; set; } = string.Empty;

    public long InventoryItemId { get; set; }

    public int Version { get; set; }
}
}
