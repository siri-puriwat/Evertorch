using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
/// One verified client content package, immutable once loaded.
/// </summary>
public sealed class ClientContent : IMapProvider
{
    private readonly IReadOnlyDictionary<MapDefinitionId, ClientMap> m_maps;

    public ClientContent(string version, IReadOnlyDictionary<MapDefinitionId, ClientMap> maps)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        m_maps = maps ?? throw new ArgumentNullException(nameof(maps));
    }

    public string Version { get; }

    public IEnumerable<ClientMap> Maps => m_maps.Values;

    public bool TryGetMap(MapDefinitionId id, out ClientMap? map)
    {
        return m_maps.TryGetValue(id, out map);
    }

    public bool TryGetNavigation(MapDefinitionId map, out NavigationGrid? grid)
    {
        bool found = m_maps.TryGetValue(map, out ClientMap? definition);
        grid = definition?.Navigation;
        return found;
    }
}
}
