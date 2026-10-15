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
    // One StatusEffects message (Network Protocol §6) carries every status at once: at most 24 entries, its header, and
    // each status's ID with 6 bytes more, within the 1,020 bytes one reliable message carries.
    private const int MaxStatusEffects = 24;
    private const int StatusListHeaderBytes = 3;
    private const int StatusEntryBytes = 6;
    private const int MaxQuests = 14;

    // One NpcServices message (Network Protocol §6): its header, then each item the NPC trades, 10 bytes and its ID's,
    // each quest it gives, and each job change it offers, those at their largest with every ID at the 64-byte limit;
    // at most 24 items.
    private const int ServicesHeaderBytes = 14;
    private const int ServicesEntryBytes = 10;
    private const int ServicesOfferBytes = 154;
    private const int ServicesJobChangeBytes = 134;
    private const int MaxServicesEntries = 24;
    private const int MaxMessageBytes = 1020;

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
        HashSet<string> refineTables = CollectIds(
            content.RefineTables,
            table => table.Definition.Id.Value,
            table => table.Source,
            diagnostics);

        AuthoredItem[] cards = content.Items.Where(item => item.Definition.Type == ItemType.Card).ToArray();
        for (int index = ContentLimits.MaxCards; index < cards.Length; index++)
        {
            Report(
                cards[index].Source,
                "type",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is one more than the {0} cards a list of removal fees carries",
                    ContentLimits.MaxCards),
                diagnostics);
        }

        HashSet<string>[] references =
            { items, monsters, skills, maps, experienceTables, statusEffects, npcs, refineTables };
        foreach (HashSet<string> known in references)
        {
            known.UnionWith(content.DeclaredIds);
        }

        RequireStatusesFit(content.StatusEffects, diagnostics);
        var statusesById = new Dictionary<string, StatusEffectDefinition>(StringComparer.Ordinal);
        foreach (AuthoredStatusEffect status in content.StatusEffects)
        {
            statusesById[status.Definition.Id.Value] = status.Definition;
        }

        HashSet<Ailment> ailments = RequireOneStatusPerAilment(content.StatusEffects, diagnostics);
        foreach (AuthoredSkill skill in content.Skills)
        {
            RequireSkillStatuses(skill, statusEffects, statusesById, diagnostics);
        }

        foreach (AuthoredItem item in content.Items)
        {
            foreach (Ailment ailment in item.Definition.Card?.Bonus.Inflict.Keys ?? Enumerable.Empty<Ailment>())
            {
                if (!ailments.Contains(ailment))
                {
                    Report(
                        item.Source,
                        "server.card.inflict." + EnumText.Of(ailment),
                        $"names ailment '{EnumText.Of(ailment)}', which no status is",
                        diagnostics);
                }
            }
        }

        // A consumable starts only a status that sets an attack element, a converter's (Content Pipeline §7).

        foreach (AuthoredItem item in content.Items)
        {
            if (item.Definition.Effect?.Status is StatusDefinitionId started)
            {
                const string startedPath = "server.effect.status.status";
                RequireReference(statusEffects, started.Value, "status effect", item.Source, startedPath, diagnostics);
                if (statusesById.TryGetValue(started.Value, out StatusEffectDefinition? status)
                    && status.AttackElement == null)
                {
                    Report(
                        item.Source,
                        startedPath,
                        $"names '{started.Value}', which sets no attack element; an item starts only such a status",
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

        RequireRefineTables(content, items, itemsById, refineTables, diagnostics);

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

    // A table's ore is a material an attempt can take whole from one row, and a table's message holds its items, at most
    // 13 (Content Pipeline §4, §7).
    private static void RequireRefineTables(
        ContentSet content,
        HashSet<string> items,
        Dictionary<string, ItemDefinition> itemsById,
        HashSet<string> refineTables,
        List<ContentDiagnostic> diagnostics)
    {
        foreach (AuthoredRefineTable table in content.RefineTables)
        {
            string material = table.Definition.Material.Value;
            RequireReference(items, material, "item", table.Source, "material", diagnostics);
            if (!itemsById.TryGetValue(material, out ItemDefinition? ore))
            {
                continue;
            }

            if (ore.Type != ItemType.Material)
            {
                Report(table.Source, "material", $"names item '{material}', which is not a material", diagnostics);
            }
            else if (table.Definition.MaterialQuantity > ore.StackLimit)
            {
                Report(
                    table.Source,
                    "materialQuantity",
                    $"must be at most the stack limit {ore.StackLimit} of item '{material}'",
                    diagnostics);
            }
        }

        var named = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (AuthoredItem item in content.Items)
        {
            if (!item.Definition.Refine.HasValue)
            {
                continue;
            }

            string table = item.Definition.Refine.Value.Value;
            RequireReference(refineTables, table, "refine table", item.Source, "server.refine", diagnostics);
            named[table] = named.TryGetValue(table, out int count) ? count + 1 : 1;
            if (named[table] > ContentLimits.MaxRefineItems)
            {
                Report(
                    item.Source,
                    "server.refine",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "is one more than the {0} items a refine table's message carries",
                        ContentLimits.MaxRefineItems),
                    diagnostics);
            }
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

    // Every status travels in one message at once, so content holds no more than it carries (Content Pipeline §4).
    private static void RequireStatusesFit(
        IReadOnlyList<AuthoredStatusEffect> statuses,
        List<ContentDiagnostic> diagnostics)
    {
        int size = StatusListHeaderBytes;
        for (int index = 0; index < statuses.Count; index++)
        {
            size += StatusEntryBytes + statuses[index].Definition.Id.Value.Length;
            if (index >= MaxStatusEffects || size > MaxMessageBytes)
            {
                Report(
                    statuses[index].Source,
                    "id",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "is one more than a status list carries: {0} status effects within {1} bytes",
                        MaxStatusEffects,
                        MaxMessageBytes),
                    diagnostics);
                return;
            }
        }
    }

    // Each ailment is one status, which a card's landing starts and a cure ends (Gameplay Systems §9.1). Returns the
    // ailments content has a status for.
    private static HashSet<Ailment> RequireOneStatusPerAilment(
        IReadOnlyList<AuthoredStatusEffect> statuses,
        List<ContentDiagnostic> diagnostics)
    {
        var first = new Dictionary<Ailment, string>();
        foreach (AuthoredStatusEffect status in statuses)
        {
            Ailment ailment = status.Definition.Ailment;
            if (ailment == Ailment.None)
            {
                continue;
            }

            if (first.TryGetValue(ailment, out string? other))
            {
                Report(
                    status.Source,
                    "server.ailment",
                    $"names ailment '{EnumText.Of(ailment)}', which status '{other}' already is",
                    diagnostics);
            }
            else
            {
                first.Add(ailment, status.Definition.Id.Value);
            }
        }

        return new HashSet<Ailment>(first.Keys);
    }

    // A skill's status exists. A rider names an ailment's status; an ailment skill targets an enemy or, as a monster's
    // area skill, the players around its caster, and changes no statistic; only an ailment skill has a chance or an
    // area among status skills (Content Pipeline §4, §7).
    private static void RequireSkillStatuses(
        AuthoredSkill skill,
        HashSet<string> knownStatuses,
        Dictionary<string, StatusEffectDefinition> statusesById,
        List<ContentDiagnostic> diagnostics)
    {
        SkillDefinition definition = skill.Definition;
        for (int index = 0; index < definition.Levels.Count; index++)
        {
            SkillEffect effect = definition.Levels[index].Effect;
            string prefix = string.Format(CultureInfo.InvariantCulture, "server.levels[{0}].effect", index);
            if (effect.Rider != null)
            {
                string riderPath = prefix + ".damage.ailment.status";
                string rider = effect.Rider.Status.Value;
                RequireReference(knownStatuses, rider, "status effect", skill.Source, riderPath, diagnostics);
                if (statusesById.TryGetValue(rider, out StatusEffectDefinition? ridden)
                    && ridden.Ailment == Ailment.None)
                {
                    Report(skill.Source, riderPath, $"names '{rider}', which is no ailment's status", diagnostics);
                }
            }

            if (effect.Kind != SkillEffectKind.Status)
            {
                continue;
            }

            string statusPath = prefix + ".status.status";
            string applied = effect.Status.Value;
            RequireReference(knownStatuses, applied, "status effect", skill.Source, statusPath, diagnostics);
            if (!statusesById.TryGetValue(applied, out StatusEffectDefinition? status))
            {
                continue;
            }

            if (status.Ailment == Ailment.None)
            {
                if (effect.ChanceBasisPoints != ContentLimits.MaxChanceBasisPoints)
                {
                    Report(skill.Source, prefix + ".status.chance", "is only for an ailment's status", diagnostics);
                }

                if (definition.HasArea)
                {
                    Report(
                        skill.Source,
                        "area",
                        $"is only for a damage or an ailment skill, and '{applied}' is no ailment's status",
                        diagnostics);
                }

                continue;
            }

            bool isOnPlayersAround = definition.TargetType == SkillTargetType.Self && definition.HasArea;
            if (definition.TargetType != SkillTargetType.Enemy && !isOnPlayersAround)
            {
                Report(
                    skill.Source,
                    "targetType",
                    $"must be enemy, or self with an area, for an ailment's status '{applied}'",
                    diagnostics);
            }

            if (effect.StatPercent != default)
            {
                Report(
                    skill.Source,
                    prefix + ".status.statPercent",
                    $"must be all 0 for an ailment's status '{applied}'",
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
            + traded.Sum(item => ServicesEntryBytes + item.Length)
            + ServicesOfferBytes * offers
            + ServicesJobChangeBytes * jobChanges;
        if (size > MaxMessageBytes)
        {
            Report(
                npc.Source,
                "id",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "offers services that take {0} bytes, more than the {1} one message carries",
                    size,
                    MaxMessageBytes),
                diagnostics);
        }

        if (traded.Count > MaxServicesEntries)
        {
            Report(
                npc.Source,
                "id",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "trades {0} items, more than the {1} one message carries",
                    traded.Count,
                    MaxServicesEntries),
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

    // A basic attack needs its damage type and strikes with the weapon's element; every other skill a job lists needs an
    // effect to resolve and keeps its statuses to its caster, and a first job's whole tree fits one skill list (Content
    // Pipeline §4, §7).
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

        if (job.Definition.BaseJob == null
            && skillsById.TryGetValue(job.Definition.BasicAttack.Value, out SkillDefinition? swing)
            && swing.DamageType != null
            && swing.Element != SkillElement.Weapon)
        {
            Report(job.Source, "server.basicAttack", "names a skill whose element is not weapon", diagnostics);
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
