namespace Evertorch.Game
{
public sealed class ItemDefinition
{
    public ItemDefinition(
        ItemDefinitionId id,
        string displayName,
        ItemType type,
        int stackLimit,
        int weight,
        int sellPrice,
        ItemEquipment? equipment = null)
    {
        Id = id;
        DisplayName = displayName;
        Type = type;
        StackLimit = stackLimit;
        Weight = weight;
        SellPrice = sellPrice;
        Equipment = equipment;
    }

    public ItemDefinitionId Id { get; }

    public string DisplayName { get; }

    public ItemType Type { get; }

    public int StackLimit { get; }

    public int Weight { get; }

    public int SellPrice { get; }

    /// <summary>A weapon's or an armor's values; null for every other type.</summary>
    public ItemEquipment? Equipment { get; }

    /// <summary>The slot a weapon or an armor fills; <see cref="EquipmentSlot.None" /> for every other type.</summary>
    public EquipmentSlot Slot => Type switch
    {
        ItemType.Weapon => EquipmentSlot.Weapon,
        ItemType.Armor => EquipmentSlot.Armor,
        _ => EquipmentSlot.None
    };
}
}
