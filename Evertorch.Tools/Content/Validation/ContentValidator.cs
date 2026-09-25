using System;
using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
///     Checks that span definitions: duplicate IDs and references between kinds. Single-field rules live in the readers.
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
        HashSet<string> experienceTables = CollectIds(
            content.ExperienceTables,
            table => table.Definition.Id.Value,
            table => table.Source,
            diagnostics);
        CollectIds(content.Jobs, job => job.Definition.Id.Value, job => job.Source, diagnostics);

        foreach (HashSet<string> known in new[] { items, monsters, skills, maps, experienceTables })
        {
            known.UnionWith(content.DeclaredIds);
        }

        var skillsById = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
        foreach (AuthoredSkill skill in content.Skills)
        {
            skillsById[skill.Definition.Id.Value] = skill.Definition;
        }

        var stackLimits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (AuthoredItem item in content.Items)
        {
            stackLimits[item.Definition.Id.Value] = item.Definition.StackLimit;
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
                RequireWithinStackLimit(monster, index, stackLimits, diagnostics);
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
            RequireReference(
                experienceTables,
                job.Definition.ExperienceTable.Value,
                "experience table",
                job.Source,
                "server.experienceTable",
                diagnostics);
            RequireJobSkills(job, skills, skillsById, diagnostics);
        }
    }

    // A basic attack needs its damage type; every other skill a job lists needs an effect to resolve (Content
    // Pipeline §7).
    private static void RequireJobSkills(
        AuthoredJob job,
        HashSet<string> knownSkills,
        Dictionary<string, SkillDefinition> skillsById,
        List<ContentDiagnostic> diagnostics)
    {
        if (skillsById.TryGetValue(job.Definition.BasicAttack.Value, out SkillDefinition? basicAttack)
            && basicAttack.DamageType == null)
        {
            Report(job.Source, "server.basicAttack", "names a skill without a damageType", diagnostics);
        }

        for (int index = 0; index < job.Definition.Skills.Count; index++)
        {
            string fieldPath = string.Format(CultureInfo.InvariantCulture, "server.skills[{0}]", index);
            string skill = job.Definition.Skills[index].Value;
            RequireReference(knownSkills, skill, "skill", job.Source, fieldPath, diagnostics);
            if (skillsById.TryGetValue(skill, out SkillDefinition? definition) && definition.Effect == null)
            {
                Report(job.Source, fieldPath, $"names skill '{skill}', which has no effect", diagnostics);
            }
        }
    }

    private static void Report(
        DefinitionSource source,
        string fieldPath,
        string message,
        List<ContentDiagnostic> diagnostics)
    {
        diagnostics.Add(new ContentDiagnostic(source.File, fieldPath, source.LineOf(fieldPath), message));
    }

    private static HashSet<string> CollectIds<T>(
        IReadOnlyList<T> definitions,
        Func<T, string> getId,
        Func<T, DefinitionSource> getSource,
        List<ContentDiagnostic> diagnostics)
    {
        var firstFileById = new Dictionary<string, string>(StringComparer.Ordinal);
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

    // A pickup is all or nothing (Gameplay Systems §11), so a drop larger than one stack could never be picked up.
    private static void RequireWithinStackLimit(
        AuthoredMonster monster,
        int index,
        Dictionary<string, int> stackLimits,
        List<ContentDiagnostic> diagnostics)
    {
        MonsterDrop drop = monster.Definition.Drops[index];
        if (!stackLimits.TryGetValue(drop.Item.Value, out int stackLimit) || drop.MaxAmount <= stackLimit)
        {
            return;
        }

        string fieldPath = string.Format(CultureInfo.InvariantCulture, "drops[{0}].amount.max", index);
        diagnostics.Add(
            new ContentDiagnostic(
                monster.Source.File,
                fieldPath,
                monster.Source.LineOf(fieldPath),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "exceeds the stack limit {0} of item '{1}'",
                    stackLimit,
                    drop.Item.Value)));
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
