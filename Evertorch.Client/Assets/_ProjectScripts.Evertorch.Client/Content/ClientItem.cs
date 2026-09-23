using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of an item definition: its name, the logical key of its view as a world drop, and its icon.
/// </summary>
public sealed class ClientItem
{
    public ClientItem(ItemDefinitionId id, string displayName, string modelKey, string iconKey)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        ModelKey = modelKey ?? throw new ArgumentNullException(nameof(modelKey));
        IconKey = iconKey ?? throw new ArgumentNullException(nameof(iconKey));
    }

    public ItemDefinitionId Id { get; }

    public string DisplayName { get; }

    public string ModelKey { get; }

    public string IconKey { get; }
}
}
