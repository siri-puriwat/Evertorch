using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
///     An item as authored: the gameplay definition plus where it came from and its client presentation keys.
/// </summary>
public sealed class AuthoredItem
{
    public AuthoredItem(DefinitionSource source, ItemDefinition definition, string icon, string model)
    {
        Source = source;
        Definition = definition;
        Icon = icon;
        Model = model;
    }

    public DefinitionSource Source { get; }

    public ItemDefinition Definition { get; }

    public string Icon { get; }

    public string Model { get; }
}
}
