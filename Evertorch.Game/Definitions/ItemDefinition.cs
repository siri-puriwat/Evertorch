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
        int sellPrice)
    {
        Id = id;
        DisplayName = displayName;
        Type = type;
        StackLimit = stackLimit;
        Weight = weight;
        SellPrice = sellPrice;
    }

    public ItemDefinitionId Id { get; }

    public string DisplayName { get; }

    public ItemType Type { get; }

    public int StackLimit { get; }

    public int Weight { get; }

    public int SellPrice { get; }
}
}
