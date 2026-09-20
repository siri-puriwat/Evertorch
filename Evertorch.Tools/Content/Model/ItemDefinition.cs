using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class ItemDefinition
{
    public ItemDefinition(
        DefinitionSource source,
        ItemDefinitionId id,
        string displayName,
        ItemType type,
        int stackLimit,
        int weight,
        int sellPrice,
        string icon,
        string model)
    {
        Source = source;
        Id = id;
        DisplayName = displayName;
        Type = type;
        StackLimit = stackLimit;
        Weight = weight;
        SellPrice = sellPrice;
        Icon = icon;
        Model = model;
    }

    public DefinitionSource Source { get; }

    public ItemDefinitionId Id { get; }

    public string DisplayName { get; }

    public ItemType Type { get; }

    public int StackLimit { get; }

    public int Weight { get; }

    public int SellPrice { get; }

    public string Icon { get; }

    public string Model { get; }
}
}
