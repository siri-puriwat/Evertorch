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
        ItemEquipment? equipment = null,
        ItemEffect? effect = null,
        ItemMarket? market = null,
        RefineDefinitionId? refine = null)
    {
        Id = id;
        DisplayName = displayName;
        Type = type;
        StackLimit = stackLimit;
        Weight = weight;
        SellPrice = sellPrice;
        Equipment = equipment;
        Effect = effect;
        Market = market;
        Refine = refine;
    }

    public ItemDefinitionId Id { get; }

    public string DisplayName { get; }

    public ItemType Type { get; }

    public int StackLimit { get; }

    public int Weight { get; }

    public int SellPrice { get; }

    /// <summary>A weapon's or an armor's values; null for every other type.</summary>
    public ItemEquipment? Equipment { get; }

    /// <summary>What a consumable restores; null for every other type.</summary>
    public ItemEffect? Effect { get; }

    /// <summary>Its terms on the market board; null when the board does not list it (Gameplay Systems §11.5).</summary>
    public ItemMarket? Market { get; }

    /// <summary>
    ///     The refine table that refines a weapon or an armor (Gameplay Systems §11.6); null when the item is not refined.
    /// </summary>
    public RefineDefinitionId? Refine { get; }

    /// <summary>The slot a weapon or an armor fills; <see cref="EquipmentSlot.None" /> for every other type.</summary>
    public EquipmentSlot Slot => Type switch
    {
        ItemType.Weapon => EquipmentSlot.Weapon,
        ItemType.Armor => EquipmentSlot.Armor,
        _ => EquipmentSlot.None
    };
}
}
