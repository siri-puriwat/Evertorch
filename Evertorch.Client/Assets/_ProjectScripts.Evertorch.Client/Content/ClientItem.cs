using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of an item definition: its name, its type, the logical key of its view as a world drop, its
///     icon, and a weapon's model in a hand.
/// </summary>
public sealed class ClientItem
{
    public ClientItem(
        ItemDefinitionId id,
        string displayName,
        ItemType type,
        string modelKey,
        string iconKey,
        string heldKey = "")
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Type = type;
        ModelKey = modelKey ?? throw new ArgumentNullException(nameof(modelKey));
        IconKey = iconKey ?? throw new ArgumentNullException(nameof(iconKey));
        HeldKey = heldKey ?? throw new ArgumentNullException(nameof(heldKey));
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
}
}
