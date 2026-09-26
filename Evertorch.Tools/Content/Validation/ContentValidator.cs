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
    // One StatusEffects message carries at most this many (Network Protocol §9).
    private const int MaxStatusEffects = 14;

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
        HashSet<string> statusEffects = CollectIds(
            content.StatusEffects,
            status => status.Definition.Id.Value,
            status => status.Source,
            diagnostics);
        CollectIds(content.Jobs, job => job.Definition.Id.Value, job => job.Source, diagnostics);

        foreach (HashSet<string> known in new[] { items, monsters, skills, maps, experienceTables, statusEffects })
        {
            known.UnionWith(content.DeclaredIds);
        }

        for (int index = MaxStatusEffects; index < content.StatusEffects.Count; index++)
        {
            Report(
                content.StatusEffects[index].Source,
                "id",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is one more than the {0} status effects a status list carries",
                    MaxStatusEffects),
                diagnostics);
        }

        foreach (AuthoredSkill skill in content.Skills)
        {
            if (skill.Definition.Effect?.Kind == SkillEffectKind.Status)
            {
                RequireReference(
                    statusEffects,
                    skill.Definition.Effect.Status.Value,
                    "status effect",
                    skill.Source,
                    "server.effect.status.status",
                    diagnostics);
            }
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
            RequireMonsterSkills(monster, skills, skillsById, diagnostics);
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

        var mapsById = new Dictionary<string, MapDefinition>(StringComparer.Ordinal);
        foreach (AuthoredMap map in content.Maps)
        {
            mapsById[map.Definition.Id.Value] = map.Definition;
        }

        var monstersById = new Dictionary<string, MonsterDefinition>(StringComparer.Ordinal);
        foreach (AuthoredMonster monster in content.Monsters)
        {
            monstersById[monster.Definition.Id.Value] = monster.Definition;
        }

        foreach (AuthoredMap map in content.Maps)
        {
            RequirePortalDestinations(map, maps, mapsById, diagnostics);
            RequireAggressiveSpawnsOutOfReach(map, content.Maps, monstersById, diagnostics);
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

    // A destination must be a place to stand on an existing map, outside every portal there, or an arrival would
    // strand the character or send it straight on (Gameplay Systems §4.2).
    private static void RequirePortalDestinations(
        AuthoredMap map,
        HashSet<string> knownMaps,
        Dictionary<string, MapDefinition> mapsById,
        List<ContentDiagnostic> diagnostics)
    {
        for (int index = 0; index < map.Definition.Portals.Count; index++)
        {
            MapPortal portal = map.Definition.Portals[index];
            string prefix = string.Format(CultureInfo.InvariantCulture, "server.portals[{0}].destination", index);
            RequireReference(
                knownMaps,
                portal.DestinationMap.Value,
                "map",
                map.Source,
                prefix + ".map",
                diagnostics);
            if (!mapsById.TryGetValue(portal.DestinationMap.Value, out MapDefinition? destination))
            {
                continue;
            }

            WorldPosition at = portal.DestinationPosition;
            NavigationGrid grid = destination.Navigation;
            if (!grid.CanOccupy(at.X, at.Z)
                || !grid.TrySampleHeight(at.X, at.Z, out float height)
                || Math.Abs(at.Y - height) > 0.01f)
            {
                Report(map.Source, prefix + ".position", "is not a place to stand on the destination map", diagnostics);
            }
            else if (destination.IsInPortal(at))
            {
                Report(map.Source, prefix + ".position", "lies inside a portal of the destination map", diagnostics);
            }
        }
    }

    // An aggressive monster idle or roaming anywhere its spawn allows must not perceive a player who has only just
    // spawned or arrived (Gameplay Systems §10), so its spawn's center lies farther than max(radius, roamRadius) +
    // perceptionRadius + 1 m from the map's spawn point and from every arrival on the map.
    private static void RequireAggressiveSpawnsOutOfReach(
        AuthoredMap map,
        IReadOnlyList<AuthoredMap> maps,
        Dictionary<string, MonsterDefinition> monstersById,
        List<ContentDiagnostic> diagnostics)
    {
        var arrivals = new List<(WorldPosition Position, string Name)>
        {
            (map.Definition.SpawnPosition, "the spawn point")
        };
        foreach (AuthoredMap origin in maps)
        {
            foreach (MapPortal portal in origin.Definition.Portals)
            {
                if (portal.DestinationMap == map.Definition.Id)
                {
                    arrivals.Add((portal.DestinationPosition, $"the arrival from {origin.Definition.Id.Value}"));
                }
            }
        }

        for (int index = 0; index < map.Definition.MonsterSpawns.Count; index++)
        {
            MonsterSpawn spawn = map.Definition.MonsterSpawns[index];
            if (!monstersById.TryGetValue(spawn.Monster.Value, out MonsterDefinition? monster)
                || monster.Behavior != MonsterBehavior.Aggressive)
            {
                continue;
            }

            double reach = Math.Max(spawn.Radius, monster.RoamRadius) + monster.PerceptionRadius + 1.0;
            foreach ((WorldPosition position, string name) in arrivals)
            {
                double deltaX = spawn.Center.X - position.X;
                double deltaZ = spawn.Center.Z - position.Z;
                if (Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ) <= reach)
                {
                    Report(
                        map.Source,
                        string.Format(CultureInfo.InvariantCulture, "server.monsterSpawns[{0}].center", index),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "is not farther than {0} m from {1}, where the aggressive {2} could perceive a player "
                            + "who has only just arrived",
                            reach,
                            name,
                            monster.Id.Value),
                        diagnostics);
                }
            }
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

    // A monster casts through the skill pipeline, so each skill it lists must resolve to something (Content
    // Pipeline §7).
    private static void RequireMonsterSkills(
        AuthoredMonster monster,
        HashSet<string> knownSkills,
        Dictionary<string, SkillDefinition> skillsById,
        List<ContentDiagnostic> diagnostics)
    {
        for (int index = 0; index < monster.Definition.Skills.Count; index++)
        {
            string fieldPath = string.Format(CultureInfo.InvariantCulture, "skills[{0}].skill", index);
            string skill = monster.Definition.Skills[index].Skill.Value;
            RequireReference(knownSkills, skill, "skill", monster.Source, fieldPath, diagnostics);
            if (skillsById.TryGetValue(skill, out SkillDefinition? definition) && definition.Effect == null)
            {
                Report(monster.Source, fieldPath, $"names skill '{skill}', which has no effect", diagnostics);
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
