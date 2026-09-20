using System;
using System.Collections.Generic;
using System.Globalization;

namespace Evertorch.Tools
{
/// <summary>
/// Checks that span definitions: duplicate IDs and references between kinds. Single-field rules live in the readers.
/// </summary>
public static class ContentValidator
{
    public static void Validate(ContentSet content, List<ContentDiagnostic> diagnostics)
    {
        HashSet<string> items = CollectIds(
            content.Items,
            item => item.Definition.Id.Value,
            item => item.Source,
            diagnostics);
        HashSet<string> monsters = CollectIds(
            content.Monsters,
            monster => monster.Definition.Id.Value,
            monster => monster.Source,
            diagnostics);
        HashSet<string> skills = CollectIds(
            content.Skills,
            skill => skill.Definition.Id.Value,
            skill => skill.Source,
            diagnostics);
        HashSet<string> maps = CollectIds(content.Maps, map => map.Definition.Id.Value, map => map.Source, diagnostics);
        CollectIds(content.Jobs, job => job.Definition.Id.Value, job => job.Source, diagnostics);

        foreach (HashSet<string> known in new[] { items, monsters, skills, maps })
        {
            known.UnionWith(content.DeclaredIds);
        }

        foreach (AuthoredMonster monster in content.Monsters)
        {
            for (int index = 0; index < monster.Definition.Drops.Count; index++)
            {
                string fieldPath = string.Format(CultureInfo.InvariantCulture, "drops[{0}].item", index);
                RequireReference(
                    items,
                    monster.Definition.Drops[index].Item.Value,
                    "item",
                    monster.Source,
                    fieldPath,
                    diagnostics);
            }
        }

        foreach (AuthoredMap map in content.Maps)
        {
            for (int index = 0; index < map.Definition.MonsterSpawns.Count; index++)
            {
                string fieldPath = string.Format(
                    CultureInfo.InvariantCulture,
                    "server.monsterSpawns[{0}].monster",
                    index);
                RequireReference(
                    monsters,
                    map.Definition.MonsterSpawns[index].Monster.Value,
                    "monster",
                    map.Source,
                    fieldPath,
                    diagnostics);
            }
        }

        foreach (AuthoredJob job in content.Jobs)
        {
            RequireReference(maps, job.Definition.StartingMap.Value, "map", job.Source, "server.startingMap",
                diagnostics);
            RequireReference(skills, job.Definition.BasicAttack.Value, "skill", job.Source, "server.basicAttack",
                diagnostics);
        }
    }

    private static HashSet<string> CollectIds<T>(
        IReadOnlyList<T> definitions,
        Func<T, string> getId,
        Func<T, DefinitionSource> getSource,
        List<ContentDiagnostic> diagnostics)
    {
        Dictionary<string, string> firstFileById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (T definition in definitions)
        {
            string id = getId(definition);
            DefinitionSource source = getSource(definition);
            if (firstFileById.TryGetValue(id, out string? firstFile))
            {
                diagnostics.Add(
                    new ContentDiagnostic(
                        source.File,
                        "id",
                        source.LineOf("id"),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "duplicate ID '{0}'; already defined in {1}",
                            id,
                            firstFile)));
                continue;
            }

            firstFileById.Add(id, source.File);
        }

        return new HashSet<string>(firstFileById.Keys, StringComparer.Ordinal);
    }

    private static void RequireReference(
        HashSet<string> knownIds,
        string referencedId,
        string kind,
        DefinitionSource source,
        string fieldPath,
        List<ContentDiagnostic> diagnostics)
    {
        if (knownIds.Contains(referencedId))
        {
            return;
        }

        diagnostics.Add(
            new ContentDiagnostic(
                source.File,
                fieldPath,
                source.LineOf(fieldPath),
                string.Format(CultureInfo.InvariantCulture, "references unknown {0} '{1}'", kind, referencedId)));
    }
}
}
