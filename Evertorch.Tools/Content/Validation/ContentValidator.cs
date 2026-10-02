using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private const int MaxQuests = 14;

    // One NpcServices message (Network Protocol §6): its header, then each item the NPC trades, each quest it gives,
    // and each job change it offers at their largest, with every ID at the 64-byte limit; one datagram carries 1,020
    // bytes.
    private const int ServicesHeaderBytes = 14;
    private const int ServicesEntryBytes = 74;
    private const int ServicesOfferBytes = 154;
    private const int ServicesJobChangeBytes = 134;
    private const int MaxServicesBytes = 1020;

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
        HashSet<string> npcs = CollectIds(content.Npcs, npc => npc.Definition.Id.Value, npc => npc.Source, diagnostics);
        CollectIds(content.Quests, quest => quest.Definition.Id.Value, quest => quest.Source, diagnostics);

        HashSet<string>[] references = { items, monsters, skills, maps, experienceTables, statusEffects, npcs };
        foreach (HashSet<string> known in references)
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
            for (int index = 0; index < skill.Definition.Levels.Count; index++)
            {
                SkillEffect effect = skill.Definition.Levels[index].Effect;
                if (effect.Kind == SkillEffectKind.Status)
                {
                    RequireReference(
                        statusEffects,
                        effect.Status.Value,
                        "status effect",
                        skill.Source,
                        string.Format(CultureInfo.InvariantCulture, "server.levels[{0}].effect.status.status", index),
                        diagnostics);
                }
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

            for (int index = 0; index < monster.Definition.MvpDrops.Count; index++)
            {
                string fieldPath = string.Format(CultureInfo.InvariantCulture, "mvpDrops[{0}].item", index);
                RequireReference(
                    items,
                    monster.Definition.MvpDrops[index].Item.Value,
                    "item",
                    monster.Source,
                    fieldPath,
                    diagnostics);
                RequirePrizeWithinStackLimit(monster, index, stackLimits, diagnostics);
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

        Dictionary<string, string> placedOn = RequireNpcsPlacedOnce(content.Maps, npcs, diagnostics);

        // A map rejected for another error may be the one that places a quest's giver.
        bool isEveryMapRead = content.DeclaredIds
            .Where(id => id.StartsWith(MapDefinitionId.KindPrefix + ".", StringComparison.Ordinal))
            .All(mapsById.ContainsKey);
        var itemsById = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (AuthoredItem item in content.Items)
        {
            itemsById[item.Definition.Id.Value] = item.Definition;
        }

        foreach (AuthoredNpc npc in content.Npcs)
        {
            RequireShop(npc, items, itemsById, diagnostics);
            RequireServicesFit(npc, content, diagnostics);
        }

        // A character keeps every quest it takes, and one quest log carries them all.
        for (int index = MaxQuests; index < content.Quests.Count; index++)
        {
            Report(
                content.Quests[index].Source,
                "id",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is one more than the {0} quests a quest log carries",
                    MaxQuests),
                diagnostics);
        }

        foreach (AuthoredQuest quest in content.Quests)
        {
            string giver = quest.Definition.Giver.Value;
            RequireReference(npcs, giver, "NPC", quest.Source, "server.giver", diagnostics);
            if (isEveryMapRead && npcs.Contains(giver) && !placedOn.ContainsKey(giver))
            {
                Report(quest.Source, "server.giver", $"names NPC '{giver}', which no map places", diagnostics);
            }

            RequireReference(
                monsters,
                quest.Definition.Monster.Value,
                "monster",
                quest.Source,
                "server.objective.kill.monster",
                diagnostics);
        }

        foreach (AuthoredJob job in content.Jobs)
        {
            // A first job's inherited values were checked on its base job.
            if (job.Definition.BaseJob == null)
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
            }

            RequireReference(
                experienceTables,
                job.Definition.JobExperienceTable.Value,
                "experience table",
                job.Source,
                "server.jobExperienceTable",
                diagnostics);
            RequireJobSkills(job, skills, skillsById, diagnostics);
        }
    }

    // Each NPC stands in one place in the whole world (Content Pipeline §4). Returns the map that places each NPC.
    private static Dictionary<string, string> RequireNpcsPlacedOnce(
        IReadOnlyList<AuthoredMap> maps,
        HashSet<string> knownNpcs,
        List<ContentDiagnostic> diagnostics)
    {
        var placedOn = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (AuthoredMap map in maps)
        {
            for (int index = 0; index < map.Definition.Npcs.Count; index++)
            {
                string fieldPath = string.Format(CultureInfo.InvariantCulture, "server.npcs[{0}].npc", index);
                string npc = map.Definition.Npcs[index].Npc.Value;
                RequireReference(knownNpcs, npc, "NPC", map.Source, fieldPath, diagnostics);
                if (placedOn.TryGetValue(npc, out string? first))
                {
                    Report(map.Source, fieldPath, $"places NPC '{npc}', which {first} already places", diagnostics);
                }
                else
                {
                    placedOn.Add(npc, map.Definition.Id.Value);
                }
            }
        }

        return placedOn;
    }

    // Each item the shop sells must exist, and no price may undercut what the shop pays for the item, so no buy and
    // sale can gain (Gameplay Systems §11.3).
    private static void RequireShop(
        AuthoredNpc npc,
        HashSet<string> knownItems,
        Dictionary<string, ItemDefinition> itemsById,
        List<ContentDiagnostic> diagnostics)
    {
        for (int index = 0; index < npc.Definition.Shop.Count; index++)
        {
            ShopEntry entry = npc.Definition.Shop[index];
            string prefix = string.Format(CultureInfo.InvariantCulture, "server.shop[{0}]", index);
            RequireReference(knownItems, entry.Item.Value, "item", npc.Source, prefix + ".item", diagnostics);
            if (itemsById.TryGetValue(entry.Item.Value, out ItemDefinition? item) && entry.Price < item.SellPrice)
            {
                Report(
                    npc.Source,
                    prefix + ".price",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "is below the sell price {0} of item '{1}'",
                        item.SellPrice,
                        item.Id.Value),
                    diagnostics);
            }
        }
    }

    // An NPC's services travel in one message: every item it trades (its stock and, with a shop, every item it buys)
    // and every quest it gives.
    private static void RequireServicesFit(AuthoredNpc npc, ContentSet content, List<ContentDiagnostic> diagnostics)
    {
        var traded = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShopEntry entry in npc.Definition.Shop)
        {
            traded.Add(entry.Item.Value);
        }

        if (npc.Definition.HasShop)
        {
            foreach (AuthoredItem item in content.Items)
            {
                if (item.Definition.SellPrice > 0)
                {
                    traded.Add(item.Definition.Id.Value);
                }
            }
        }

        int offers = 0;
        foreach (AuthoredQuest quest in content.Quests)
        {
            if (quest.Definition.Giver == npc.Definition.Id)
            {
                offers++;
            }
        }

        int jobChanges = npc.Definition.OffersJobChange
            ? content.Jobs.Count(job => job.Definition.BaseJob != null)
            : 0;
        int size = ServicesHeaderBytes
            + ServicesEntryBytes * traded.Count
            + ServicesOfferBytes * offers
            + ServicesJobChangeBytes * jobChanges;
        if (size > MaxServicesBytes)
        {
            Report(
                npc.Source,
                "id",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "offers services that take {0} bytes, more than the {1} one message carries",
                    size,
                    MaxServicesBytes),
                diagnostics);
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

    // A basic attack needs its damage type; every other skill a job lists needs an effect to resolve and keeps its
    // statuses to its caster, and a first job's whole tree fits one skill list (Content Pipeline §7).
    private static void RequireJobSkills(
        AuthoredJob job,
        HashSet<string> knownSkills,
        Dictionary<string, SkillDefinition> skillsById,
        List<ContentDiagnostic> diagnostics)
    {
        if (job.Definition.BaseJob == null
            && skillsById.TryGetValue(job.Definition.BasicAttack.Value, out SkillDefinition? basicAttack)
            && basicAttack.DamageType == null)
        {
            Report(job.Source, "server.basicAttack", "names a skill without a damageType", diagnostics);
        }

        if (job.Definition.Tree.Count > JobDefinitionReader.MaxSkills)
        {
            Report(
                job.Source,
                "server.skills",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "makes a whole tree of {0} skills with its base job's, more than the {1} a skill list carries",
                    job.Definition.Tree.Count,
                    JobDefinitionReader.MaxSkills),
                diagnostics);
        }

        for (int index = 0; index < job.Definition.Skills.Count; index++)
        {
            string fieldPath = string.Format(CultureInfo.InvariantCulture, "server.skills[{0}]", index);
            string skill = job.Definition.Skills[index].Value;
            RequireReference(knownSkills, skill, "skill", job.Source, fieldPath, diagnostics);
            if (skillsById.TryGetValue(skill, out SkillDefinition? definition) && !definition.HasEffect)
            {
                Report(job.Source, fieldPath, $"names skill '{skill}', which has no effect", diagnostics);
            }
            else if (definition != null && definition.HasArea)
            {
                Report(
                    job.Source,
                    fieldPath,
                    $"names skill '{skill}', which strikes an area; only a monster casts one",
                    diagnostics);
            }
            else if (definition != null && IsStatusOnAnEnemy(definition))
            {
                Report(
                    job.Source,
                    fieldPath,
                    $"names skill '{skill}', which puts a status on an enemy; a job's statuses are its own",
                    diagnostics);
            }

            if (definition?.Requires != null)
            {
                RequirePrerequisite(job, definition, skillsById, fieldPath, diagnostics);
            }
        }
    }

    // A prerequisite is another skill of the job's whole tree at one of its levels, and prerequisites form no cycle
    // (Content Pipeline §4).
    private static void RequirePrerequisite(
        AuthoredJob job,
        SkillDefinition skill,
        Dictionary<string, SkillDefinition> skillsById,
        string fieldPath,
        List<ContentDiagnostic> diagnostics)
    {
        // A required skill rejected for its own problem is reported there alone (§7).
        SkillRequirement requires = skill.Requires!;
        if (requires.Skill == skill.Id || !job.Definition.Tree.Contains(requires.Skill))
        {
            Report(
                job.Source,
                fieldPath,
                $"names skill '{skill.Id.Value}', which requires '{requires.Skill.Value}', not another of the tree",
                diagnostics);
            return;
        }

        if (!skillsById.TryGetValue(requires.Skill.Value, out SkillDefinition? required))
        {
            return;
        }

        if (requires.Level > required.MaxLevel)
        {
            Report(
                job.Source,
                fieldPath,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "names skill '{0}', which requires '{1}' at level {2}, above its maximum {3}",
                    skill.Id.Value,
                    requires.Skill.Value,
                    requires.Level,
                    required.MaxLevel),
                diagnostics);
        }

        var seen = new HashSet<SkillDefinitionId> { skill.Id };
        SkillRequirement? next = requires;
        while (next != null && skillsById.TryGetValue(next.Skill.Value, out SkillDefinition? step))
        {
            if (!seen.Add(next.Skill))
            {
                Report(
                    job.Source,
                    fieldPath,
                    $"names skill '{skill.Id.Value}', which is in a cycle of prerequisites",
                    diagnostics);
                return;
            }

            next = step.Requires;
        }
    }

    private static bool IsStatusOnAnEnemy(SkillDefinition skill)
    {
        return skill.TargetType == SkillTargetType.Enemy
            && skill.Levels.Count > 0
            && skill.Levels[0].Effect.Kind == SkillEffectKind.Status;
    }

    // A monster casts through the skill pipeline at the player it fights, so each skill it lists must resolve to
    // something and be cast at an enemy, or strike the area around the monster (Content Pipeline §7).
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
            if (!skillsById.TryGetValue(skill, out SkillDefinition? definition))
            {
                continue;
            }

            if (!definition.HasEffect)
            {
                Report(monster.Source, fieldPath, $"names skill '{skill}', which has no effect", diagnostics);
            }
            else if (definition.TargetType != SkillTargetType.Enemy && !definition.HasArea)
            {
                Report(
                    monster.Source,
                    fieldPath,
                    $"names skill '{skill}', which is neither cast at an enemy nor around the monster",
                    diagnostics);
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

    // A prize goes into one row of the bag, or lies on the ground whole (Gameplay Systems §10, §11).
    private static void RequirePrizeWithinStackLimit(
        AuthoredMonster monster,
        int index,
        Dictionary<string, int> stackLimits,
        List<ContentDiagnostic> diagnostics)
    {
        MvpDrop prize = monster.Definition.MvpDrops[index];
        if (!stackLimits.TryGetValue(prize.Item.Value, out int stackLimit) || prize.Amount <= stackLimit)
        {
            return;
        }

        string fieldPath = string.Format(CultureInfo.InvariantCulture, "mvpDrops[{0}].amount", index);
        diagnostics.Add(
            new ContentDiagnostic(
                monster.Source.File,
                fieldPath,
                monster.Source.LineOf(fieldPath),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "exceeds the stack limit {0} of item '{1}'",
                    stackLimit,
                    prize.Item.Value)));
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
