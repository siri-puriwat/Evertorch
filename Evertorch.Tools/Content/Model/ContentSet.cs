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
        IEnumerable<AuthoredItem> items,
        IEnumerable<AuthoredMonster> monsters,
        IEnumerable<AuthoredSkill> skills,
        IEnumerable<AuthoredJob> jobs,
        IEnumerable<AuthoredMap> maps,
        IEnumerable<string> declaredIds)
    {
        Items = items.OrderBy(item => item.Definition.Id.Value, StringComparer.Ordinal).ToList();
        Monsters = monsters.OrderBy(monster => monster.Definition.Id.Value, StringComparer.Ordinal).ToList();
        Skills = skills.OrderBy(skill => skill.Definition.Id.Value, StringComparer.Ordinal).ToList();
        Jobs = jobs.OrderBy(job => job.Definition.Id.Value, StringComparer.Ordinal).ToList();
        Maps = maps.OrderBy(map => map.Definition.Id.Value, StringComparer.Ordinal).ToList();
        DeclaredIds = new HashSet<string>(declaredIds, StringComparer.Ordinal);
    }

    public IReadOnlyList<AuthoredItem> Items { get; }

    public IReadOnlyList<AuthoredMonster> Monsters { get; }

    public IReadOnlyList<AuthoredSkill> Skills { get; }

    public IReadOnlyList<AuthoredJob> Jobs { get; }

    public IReadOnlyList<AuthoredMap> Maps { get; }

    /// <summary>
    /// Every valid ID some file declared, including files rejected for another error. A reference to one of these
    /// is not reported as unknown, so one mistake does not surface as several.
    /// </summary>
    public IReadOnlyCollection<string> DeclaredIds { get; }
}
}
