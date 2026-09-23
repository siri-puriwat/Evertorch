using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     One verified client content package, immutable once loaded.
/// </summary>
public sealed class ClientContent : IMapProvider
{
    private readonly IReadOnlyDictionary<MapDefinitionId, ClientMap> m_maps;
    private readonly IReadOnlyDictionary<JobDefinitionId, ClientJob> m_jobs;
    private readonly IReadOnlyDictionary<MonsterDefinitionId, ClientMonster> m_monsters;
    private readonly IReadOnlyDictionary<ItemDefinitionId, ClientItem> m_items;

    public ClientContent(
        string version,
        IReadOnlyDictionary<MapDefinitionId, ClientMap> maps,
        IReadOnlyDictionary<JobDefinitionId, ClientJob> jobs,
        IReadOnlyDictionary<MonsterDefinitionId, ClientMonster> monsters,
        IReadOnlyDictionary<ItemDefinitionId, ClientItem> items)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        m_maps = maps ?? throw new ArgumentNullException(nameof(maps));
        m_jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        m_monsters = monsters ?? throw new ArgumentNullException(nameof(monsters));
        m_items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public string Version { get; }

    public IEnumerable<ClientMap> Maps => m_maps.Values;

    public IEnumerable<ClientJob> Jobs => m_jobs.Values;

    public IEnumerable<ClientMonster> Monsters => m_monsters.Values;

    public IEnumerable<ClientItem> Items => m_items.Values;

    public bool TryGetNavigation(MapDefinitionId map, out NavigationGrid? grid)
    {
        bool found = m_maps.TryGetValue(map, out ClientMap? definition);
        grid = definition?.Navigation;
        return found;
    }

    public bool TryGetMap(MapDefinitionId id, out ClientMap? map)
    {
        return m_maps.TryGetValue(id, out map);
    }

    public bool TryGetJob(JobDefinitionId id, out ClientJob? job)
    {
        return m_jobs.TryGetValue(id, out job);
    }

    public bool TryGetMonster(MonsterDefinitionId id, out ClientMonster? monster)
    {
        return m_monsters.TryGetValue(id, out monster);
    }

    public bool TryGetItem(ItemDefinitionId id, out ClientItem? item)
    {
        return m_items.TryGetValue(id, out item);
    }
}
}
