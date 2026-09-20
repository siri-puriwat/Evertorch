using System;
using System.Collections.Generic;
using System.Linq;

namespace Evertorch.Tools
{
/// <summary>
/// The normalized content model. Every list is ordered by ordinal ID so output never depends on file-system order.
/// </summary>
public sealed class ContentSet
{
    public ContentSet(
        IEnumerable<ItemDefinition> items,
        IEnumerable<MonsterDefinition> monsters,
        IEnumerable<SkillDefinition> skills,
        IEnumerable<JobDefinition> jobs,
        IEnumerable<MapDefinition> maps,
        IEnumerable<string> declaredIds)
    {
        Items = items.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToList();
        Monsters = monsters.OrderBy(monster => monster.Id.Value, StringComparer.Ordinal).ToList();
        Skills = skills.OrderBy(skill => skill.Id.Value, StringComparer.Ordinal).ToList();
        Jobs = jobs.OrderBy(job => job.Id.Value, StringComparer.Ordinal).ToList();
        Maps = maps.OrderBy(map => map.Id.Value, StringComparer.Ordinal).ToList();
        DeclaredIds = new HashSet<string>(declaredIds, StringComparer.Ordinal);
    }

    public IReadOnlyList<ItemDefinition> Items { get; }

    public IReadOnlyList<MonsterDefinition> Monsters { get; }

    public IReadOnlyList<SkillDefinition> Skills { get; }

    public IReadOnlyList<JobDefinition> Jobs { get; }

    public IReadOnlyList<MapDefinition> Maps { get; }

    /// <summary>
    /// Every valid ID some file declared, including files rejected for another error. A reference to one of these
    /// is not reported as unknown, so one mistake does not surface as several.
    /// </summary>
    public IReadOnlyCollection<string> DeclaredIds { get; }
}
}
