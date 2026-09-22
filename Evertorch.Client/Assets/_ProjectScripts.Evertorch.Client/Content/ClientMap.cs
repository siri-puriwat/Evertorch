using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a map definition: what to show and where a body can walk.
/// </summary>
public sealed class ClientMap
{
    public ClientMap(MapDefinitionId id, string displayName, string sceneKey, NavigationGrid navigation)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        SceneKey = sceneKey ?? throw new ArgumentNullException(nameof(sceneKey));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    public MapDefinitionId Id { get; }

    public string DisplayName { get; }

    public string SceneKey { get; }

    public NavigationGrid Navigation { get; }
}
}
