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
        RefineDefinitionId? refine = null,
        int slots = 0,
        ItemCard? card = null)
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
        Slots = slots;
        Card = card;
    }

    public ItemDefinitionId Id { get; }

    public string DisplayName { get; }

    public ItemType Type { get; }

    public int StackLimit { get; }

    public int Weight { get; }

    public int SellPrice { get; }

    /// <summary>A piece of equipment's values; null for every other type.</summary>
    public ItemEquipment? Equipment { get; }

    /// <summary>What a consumable restores; null for every other type.</summary>
    public ItemEffect? Effect { get; }

    /// <summary>Its terms on the market board; null when the board does not list it (Gameplay Systems §11.5).</summary>
    public ItemMarket? Market { get; }

    /// <summary>
    ///     The refine table that refines a piece of equipment (Gameplay Systems §11.6); null when the item is not refined.
    /// </summary>
    public RefineDefinitionId? Refine { get; }

    /// <summary>
    ///     The cards a piece of equipment holds (Gameplay Systems §11.7); 0 for every other type.
    /// </summary>
    public int Slots { get; }

    /// <summary>A card's facts; null for every other type.</summary>
    public ItemCard? Card { get; }

    /// <summary>Whether the item is worn: a weapon, armor, or a piece of another category (Gameplay Systems §11.1).</summary>
    public bool IsEquipment => EquipmentPositions.IsEquipment(Type);
}
}
