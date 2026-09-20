using System.Collections.Generic;
using System.Collections.ObjectModel;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
/// One validated server content package. It never changes after loading; a new package means a new instance.
/// </summary>
public sealed class ServerContent
{
    public ServerContent(
        string serverContentVersion,
        string clientContentVersion,
        IDictionary<ItemDefinitionId, ItemDefinition> items,
        IDictionary<MonsterDefinitionId, MonsterDefinition> monsters,
        IDictionary<SkillDefinitionId, SkillDefinition> skills,
        IDictionary<JobDefinitionId, JobDefinition> jobs,
        IDictionary<MapDefinitionId, MapDefinition> maps)
    {
        ServerContentVersion = serverContentVersion;
        ClientContentVersion = clientContentVersion;
        Items = Freeze(items);
        Monsters = Freeze(monsters);
        Skills = Freeze(skills);
        Jobs = Freeze(jobs);
        Maps = Freeze(maps);
    }

    public string ServerContentVersion { get; }

    /// <summary>
    /// The client package version built from the same canonical content; the only one this server admits.
    /// </summary>
    public string ClientContentVersion { get; }

    public IReadOnlyDictionary<ItemDefinitionId, ItemDefinition> Items { get; }

    public IReadOnlyDictionary<MonsterDefinitionId, MonsterDefinition> Monsters { get; }

    public IReadOnlyDictionary<SkillDefinitionId, SkillDefinition> Skills { get; }

    public IReadOnlyDictionary<JobDefinitionId, JobDefinition> Jobs { get; }

    public IReadOnlyDictionary<MapDefinitionId, MapDefinition> Maps { get; }

    private static IReadOnlyDictionary<TKey, TValue> Freeze<TKey, TValue>(IDictionary<TKey, TValue> source)
        where TKey : notnull
    {
        return new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(source));
    }
}
}
