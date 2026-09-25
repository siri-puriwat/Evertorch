using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a status effect: its name and the optional logical key of its icon.
/// </summary>
public sealed class ClientStatusEffect
{
    public ClientStatusEffect(StatusDefinitionId id, string displayName, string? iconKey)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        IconKey = iconKey;
    }

    public StatusDefinitionId Id { get; }

    public string DisplayName { get; }

    public string? IconKey { get; }
}
}
