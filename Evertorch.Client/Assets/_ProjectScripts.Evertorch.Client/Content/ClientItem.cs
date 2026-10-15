using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of an item definition: its name, its type, how many a stack holds, the logical key of its view
///     as a world drop, its icon, a weapon's model in a hand, the cards a piece of equipment holds, and a card's fit,
///     prefix, and description, which the player sees.
/// </summary>
public sealed class ClientItem
{
    public ClientItem(
        ItemDefinitionId id,
        string displayName,
        ItemType type,
        string modelKey,
        string iconKey,
        string heldKey = "",
        int stackLimit = 1,
        int slots = 0,
        ItemType fits = ItemType.Material,
        string prefix = "",
        string description = "")
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Type = type;
        ModelKey = modelKey ?? throw new ArgumentNullException(nameof(modelKey));
        IconKey = iconKey ?? throw new ArgumentNullException(nameof(iconKey));
        HeldKey = heldKey ?? throw new ArgumentNullException(nameof(heldKey));
        StackLimit = stackLimit;
        Slots = slots;
        Fits = fits;
        Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public ItemDefinitionId Id { get; }

    public string DisplayName { get; }

    /// <summary>
    ///     What a press of the item's inventory row asks for (<see cref="InventoryActions" />).
    /// </summary>
    public ItemType Type { get; }

    public string ModelKey { get; }

    public string IconKey { get; }

    /// <summary>
    ///     A weapon's model in a hand (Prototype Content §2); empty when the item names none.
    /// </summary>
    public string HeldKey { get; }

    /// <summary>
    ///     The most one row holds, so an amount can be clamped before the server would refuse it.
    /// </summary>
    public int StackLimit { get; }

    /// <summary>
    ///     The cards a piece of equipment holds, " [2]" in its name (Prototype Content §2); 0 for every other item.
    /// </summary>
    public int Slots { get; }

    /// <summary>
    ///     The category of equipment a card fits; <see cref="ItemType.Material" />, no category, for every other item.
    /// </summary>
    public ItemType Fits { get; }

    /// <summary>
    ///     A card's word in the names of the items holding it, "Keen"; empty for every other item.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    ///     What a card does, for the Card window; empty for every other item.
    /// </summary>
    public string Description { get; }
}
}
