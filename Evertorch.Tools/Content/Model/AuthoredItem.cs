using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
///     An item as authored: the gameplay definition plus where it came from and its client presentation keys.
/// </summary>
public sealed class AuthoredItem
{
    public AuthoredItem(
        DefinitionSource source,
        ItemDefinition definition,
        string icon,
        string model,
        string? held = null,
        string? prefix = null,
        string? description = null)
    {
        Source = source;
        Definition = definition;
        Icon = icon;
        Model = model;
        Held = held;
        Prefix = prefix;
        Description = description;
    }

    public DefinitionSource Source { get; }

    public ItemDefinition Definition { get; }

    public string Icon { get; }

    public string Model { get; }

    /// <summary>
    ///     A weapon's model in a hand (Content Pipeline §4); null when the item names none.
    /// </summary>
    public string? Held { get; }

    /// <summary>
    ///     A card's word in the names of the items holding it (Prototype Content §2); null for every other item.
    /// </summary>
    public string? Prefix { get; }

    /// <summary>
    ///     What a card does, for the player (Content Pipeline §4); null for every other item.
    /// </summary>
    public string? Description { get; }
}
}
