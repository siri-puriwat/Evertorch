using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Loads the server content package and rejects it unless it is complete, untampered, and internally consistent.
///     The tools already validate canonical content; this guards the package the server was actually given.
/// </summary>
public static class ServerContentLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string ManifestFile = "manifest.json";

    private const string ExperienceFile = "experience.json";
    private const string ItemsFile = "items.json";
    private const string JobsFile = "jobs.json";
    private const string MapsFile = "maps.json";
    private const string MonstersFile = "monsters.json";
    private const string NpcsFile = "npcs.json";
    private const string QuestsFile = "quests.json";
    private const string SkillsFile = "skills.json";
    private const string StatusEffectsFile = "status-effects.json";
    private const int ContentVersionLength = 16;
    private const float GroundHeightTolerance = 0.01f;

    // One NpcServices message (Network Protocol §6): its header, then each item the NPC trades, each quest it gives,
    // and each job change it offers at their largest, with every ID at the 64-byte limit; one datagram carries 1,020
    // bytes. The tools check the same.
    private const int ServicesHeaderBytes = 14;
    private const int ServicesEntryBytes = 74;
    private const int ServicesOfferBytes = 154;
    private const int ServicesJobChangeBytes = 134;
    private const int MaxServicesBytes = 1020;

    // What a first job takes from its base job, so its entry omits them (Content Pipeline §4).
    private static readonly string[] InheritedJobFields =
    {
        "startingStats", "startingMap", "experienceTable", "basicAttack", "baseSpeed", "unarmedAttackSpeedPenalty"
    };

    private static readonly string[] DataFiles =
    {
        ExperienceFile, ItemsFile, JobsFile, MapsFile, MonstersFile, NpcsFile, QuestsFile, SkillsFile,
        StatusEffectsFile
    };

    public static ServerContent LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new ContentLoadException(new[] { $"{directory}: the content package directory does not exist" });
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            files[Path.GetFileName(path)] = File.ReadAllBytes(path);
        }

        return Load(files);
    }

    public static ServerContent Load(IReadOnlyDictionary<string, byte[]> files)
    {
        var problems = new List<string>();

        PackageManifest? manifest = ReadManifest(files, problems);
        if (manifest == null)
        {
            throw new ContentLoadException(problems);
        }

        CheckFiles(files, manifest, problems);
        if (problems.Count != 0)
        {
            // Parsing files that are missing or do not match their hash would only bury the real defect.
            throw new ContentLoadException(problems);
        }

        var items = new Dictionary<ItemDefinitionId, ItemDefinition>();
        var monsters =
            new Dictionary<MonsterDefinitionId, MonsterDefinition>();
        var skills = new Dictionary<SkillDefinitionId, SkillDefinition>();
        var jobEntries = new Dictionary<JobDefinitionId, JobEntry>();
        var maps = new Dictionary<MapDefinitionId, MapDefinition>();
        var experienceTables = new Dictionary<ExperienceDefinitionId, ExperienceTableDefinition>();
        var statusEffects = new Dictionary<StatusDefinitionId, StatusEffectDefinition>();
        var npcs = new Dictionary<NpcDefinitionId, NpcDefinition>();
        var quests = new Dictionary<QuestDefinitionId, QuestDefinition>();

        HashSet<ItemDefinitionId> declaredItems = ReadDefinitions(
            files,
            ItemsFile,
            problems,
            items,
            ItemDefinitionId.TryCreate,
            ReadItem);
        HashSet<MonsterDefinitionId> declaredMonsters = ReadDefinitions(
            files,
            MonstersFile,
            problems,
            monsters,
            MonsterDefinitionId.TryCreate,
            ReadMonster);
        HashSet<SkillDefinitionId> declaredSkills = ReadDefinitions(
            files,
            SkillsFile,
            problems,
            skills,
            SkillDefinitionId.TryCreate,
            ReadSkill);
        HashSet<JobDefinitionId> declaredJobs = ReadDefinitions(
            files,
            JobsFile,
            problems,
            jobEntries,
            JobDefinitionId.TryCreate,
            ReadJob);
        HashSet<MapDefinitionId> declaredMaps = ReadDefinitions(
            files,
            MapsFile,
            problems,
            maps,
            MapDefinitionId.TryCreate,
            ReadMap);
        HashSet<ExperienceDefinitionId> declaredExperienceTables = ReadDefinitions(
            files,
            ExperienceFile,
            problems,
            experienceTables,
            ExperienceDefinitionId.TryCreate,
            ReadExperienceTable);
        HashSet<StatusDefinitionId> declaredStatusEffects = ReadDefinitions(
            files,
            StatusEffectsFile,
            problems,
            statusEffects,
            StatusDefinitionId.TryCreate,
            ReadStatusEffect);
        HashSet<NpcDefinitionId> declaredNpcs = ReadDefinitions(
            files,
            NpcsFile,
            problems,
            npcs,
            NpcDefinitionId.TryCreate,
            ReadNpc);
        ReadDefinitions(
            files,
            QuestsFile,
            problems,
            quests,
            QuestDefinitionId.TryCreate,
            ReadQuest);
        Dictionary<JobDefinitionId, JobDefinition> jobs =
            ResolveJobs(jobEntries, experienceTables, declaredJobs, problems);
        if (declaredStatusEffects.Count > StatusEffects.MaxEntries)
        {
            problems.Add(
                $"{StatusEffectsFile}: lists more than the {StatusEffects.MaxEntries} status effects a status list "
                + "carries");
        }

        // A character keeps every quest it takes, and one quest log carries them all.
        if (quests.Count > QuestLog.MaxEntries)
        {
            problems.Add($"{QuestsFile}: lists more than the {QuestLog.MaxEntries} quests a quest log carries");
        }

        CheckReferences(
            monsters.Values,
            jobs.Values,
            maps.Values,
            skills,
            items,
            declaredItems,
            declaredMonsters,
            declaredSkills,
            declaredMaps,
            declaredExperienceTables,
            declaredStatusEffects,
            problems);
        CheckNpcsAndQuests(
            npcs.Values,
            quests.Values,
            maps.Values,
            jobs.Values.Count(job => job.BaseJob != null),
            items,
            declaredItems,
            declaredMonsters,
            declaredNpcs,
            maps.Count == declaredMaps.Count,
            problems);

        if (problems.Count != 0)
        {
            throw new ContentLoadException(problems);
        }

        return new ServerContent(
            manifest.ServerContentVersion,
            manifest.ClientContentVersion,
            items,
            monsters,
            skills,
            jobs,
            maps,
            experienceTables,
            statusEffects,
            npcs,
            quests);
    }

    private static PackageManifest? ReadManifest(IReadOnlyDictionary<string, byte[]> files, List<string> problems)
    {
        if (!files.TryGetValue(ManifestFile, out byte[]? content))
        {
            problems.Add($"{ManifestFile}: the package has no manifest");
            return null;
        }

        var root = PackageObjectReader.ForRoot(content, ManifestFile, problems);
        if (root == null)
        {
            return null;
        }

        int problemsBefore = problems.Count;
        int schemaVersion = root.RequiredInt("schemaVersion", 0);
        string serverVersion = root.RequiredString("serverContentVersion");
        string clientVersion = root.RequiredString("clientContentVersion");

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PackageObjectReader entry in root.RequiredObjectArray("files"))
        {
            string path = entry.RequiredString("path");
            string hash = entry.RequiredString("sha256");
            entry.ReportUnexpectedProperties();
            if (hashes.ContainsKey(path))
            {
                entry.Report("path", $"'{path}' is listed more than once");
            }

            hashes[path] = hash;
        }

        root.ReportUnexpectedProperties();
        if (problems.Count != problemsBefore)
        {
            return null;
        }

        if (schemaVersion != SupportedSchemaVersion)
        {
            root.Report(
                "schemaVersion",
                $"schema version {schemaVersion} is not supported; expected {SupportedSchemaVersion}");
        }

        if (!IsContentVersion(serverVersion))
        {
            root.Report("serverContentVersion", "must be 16 lowercase hexadecimal digits");
        }

        if (!IsContentVersion(clientVersion))
        {
            root.Report("clientContentVersion", "must be 16 lowercase hexadecimal digits");
        }

        return problems.Count == problemsBefore ? new PackageManifest(serverVersion, clientVersion, hashes) : null;
    }

    private static void CheckFiles(
        IReadOnlyDictionary<string, byte[]> files,
        PackageManifest manifest,
        List<string> problems)
    {
        foreach (string expected in DataFiles)
        {
            if (!manifest.Hashes.ContainsKey(expected))
            {
                problems.Add($"{ManifestFile}: files: '{expected}' is not listed");
            }
        }

        IEnumerable<KeyValuePair<string, string>> listedFiles = manifest.Hashes.OrderBy(
            pair => pair.Key,
            StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> listed in listedFiles)
        {
            if (Array.IndexOf(DataFiles, listed.Key) < 0)
            {
                problems.Add($"{ManifestFile}: files: '{listed.Key}' is not a known data file");
            }
            else if (!files.TryGetValue(listed.Key, out byte[]? content))
            {
                problems.Add($"{listed.Key}: listed in the manifest but missing from the package");
            }
            else if (!string.Equals(ComputeHash(content), listed.Value, StringComparison.Ordinal))
            {
                problems.Add($"{listed.Key}: content does not match the SHA-256 recorded in the manifest");
            }
        }

        foreach (string present in files.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (present != ManifestFile && !manifest.Hashes.ContainsKey(present))
            {
                problems.Add($"{present}: not listed in the manifest");
            }
        }

        if (problems.Count == 0
            && !string.Equals(ComputeVersion(manifest.Hashes), manifest.ServerContentVersion, StringComparison.Ordinal))
        {
            problems.Add($"{ManifestFile}: serverContentVersion: does not match the listed files");
        }
    }

    /// <summary>
    ///     Returns every ID the file declared, including definitions rejected for another defect, so that one defect is
    ///     not also reported as an unknown reference wherever that definition is used.
    /// </summary>
    private static HashSet<TId> ReadDefinitions<TId, TDefinition>(
        IReadOnlyDictionary<string, byte[]> files,
        string file,
        List<string> problems,
        Dictionary<TId, TDefinition> definitions,
        TryCreatePackageId<TId> tryCreateId,
        Func<PackageObjectReader, TId, List<string>, TDefinition?> read)
        where TId : struct
        where TDefinition : class
    {
        var declared = new HashSet<TId>();
        var root = PackageObjectReader.ForRoot(files[file], file, problems);
        if (root == null)
        {
            return declared;
        }

        int schemaVersion = root.RequiredInt("schemaVersion", 0);
        if (schemaVersion != SupportedSchemaVersion)
        {
            root.Report("schemaVersion", $"schema version {schemaVersion} is not supported");
        }

        foreach (PackageObjectReader entry in root.RequiredObjectArray("definitions"))
        {
            TId id = entry.RequiredId("id", tryCreateId);
            bool hasId = !EqualityComparer<TId>.Default.Equals(id, default);
            if (hasId && !declared.Add(id))
            {
                entry.Report("id", $"'{id}' is defined more than once");
                continue;
            }

            TDefinition? definition = read(entry, id, problems);
            if (definition != null && hasId)
            {
                definitions.Add(id, definition);
            }
        }

        root.ReportUnexpectedProperties();
        return declared;
    }

    private static ItemDefinition? ReadItem(PackageObjectReader entry, ItemDefinitionId id, List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        ItemType type = entry.RequiredEnum<ItemType>("type");
        int stackLimit = entry.RequiredInt("stackLimit", 1, ContentLimits.MaxStack);
        int weight = entry.RequiredInt("weight", 0, ContentLimits.MaxStack);
        int sellPrice = entry.RequiredInt("sellPrice", 0, ContentLimits.MaxPrice);
        ItemEquipment? equipment = null;
        bool isEquipment = type == ItemType.Weapon || type == ItemType.Armor;
        if (isEquipment && stackLimit != 1)
        {
            entry.Report("stackLimit", "must be 1 for a weapon or armor");
        }

        if (entry.Has("equipment"))
        {
            equipment = ReadEquipment(entry.RequiredObject("equipment"), type == ItemType.Weapon);
            if (!isEquipment)
            {
                entry.Report("equipment", "is only for a weapon or armor");
            }
        }
        else if (isEquipment)
        {
            entry.Report("equipment", "required property is missing for a weapon or armor");
        }

        ItemEffect? effect = null;
        if (entry.Has("effect"))
        {
            effect = ReadEffect(entry, entry.RequiredObject("effect"));
            if (type != ItemType.Consumable)
            {
                entry.Report("effect", "is only for a consumable");
            }
        }
        else if (type == ItemType.Consumable)
        {
            entry.Report("effect", "required property is missing for a consumable");
        }

        entry.ReportUnexpectedProperties();

        return problems.Count == problemsBefore
            ? new ItemDefinition(id, displayName, type, stackLimit, weight, sellPrice, equipment, effect)
            : null;
    }

    private static ItemEffect? ReadEffect(PackageObjectReader entry, PackageObjectReader? values)
    {
        if (values == null)
        {
            return null;
        }

        int health = values.RequiredInt("hp", 0, ContentLimits.MaxStat);
        int spirit = values.RequiredInt("sp", 0, ContentLimits.MaxStat);
        values.ReportUnexpectedProperties();
        if (health == 0 && spirit == 0)
        {
            entry.Report("effect", "must restore HP, SP, or both");
            return null;
        }

        return new ItemEffect(health, spirit);
    }

    // A weapon names its type and keeps its attack-speed penalty within a job's limit (Gameplay Systems §11.1).
    private static ItemEquipment? ReadEquipment(PackageObjectReader? values, bool isWeapon)
    {
        PackageObjectReader? bonus = values?.RequiredObject("bonus");
        if (values == null || bonus == null)
        {
            return null;
        }

        var stats = new PrimaryStats(
            bonus.RequiredInt("str", 0, ContentLimits.MaxStat),
            bonus.RequiredInt("agi", 0, ContentLimits.MaxStat),
            bonus.RequiredInt("vit", 0, ContentLimits.MaxStat),
            bonus.RequiredInt("int", 0, ContentLimits.MaxStat),
            bonus.RequiredInt("dex", 0, ContentLimits.MaxStat),
            bonus.RequiredInt("luk", 0, ContentLimits.MaxStat));
        bonus.ReportUnexpectedProperties();
        WeaponType? weaponType = null;
        if (isWeapon)
        {
            weaponType = values.RequiredEnum<WeaponType>("weaponType");
        }
        else if (values.Has("weaponType"))
        {
            values.Report("weaponType", "is only for a weapon");
        }

        var equipment = new ItemEquipment(
            values.RequiredInt("attack", 0, ContentLimits.MaxStat),
            values.RequiredInt("attackSpeedPenalty", 0, ContentLimits.MaxAttackSpeedPenalty),
            values.RequiredInt("defense", 0, ContentLimits.MaxStat),
            stats,
            weaponType);
        values.ReportUnexpectedProperties();
        return equipment;
    }

    private static MonsterDefinition? ReadMonster(
        PackageObjectReader entry,
        MonsterDefinitionId id,
        List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        int level = entry.RequiredInt("level", 1, ContentLimits.MaxLevel);
        bool isBoss = entry.RequiredBool("boss");
        int hp = entry.RequiredInt("hp", 1, ContentLimits.MaxHp);
        int physicalAttack = entry.RequiredInt("physicalAttack", 0, ContentLimits.MaxStat);
        int physicalDefense = entry.RequiredInt("physicalDefense", 0, ContentLimits.MaxStat);
        int hit = entry.RequiredInt("hit", 0, ContentLimits.MaxStat);
        int flee = entry.RequiredInt("flee", 0, ContentLimits.MaxStat);
        int magicAttack = entry.RequiredInt("magicAttack", 0, ContentLimits.MaxStat);
        double baseSpeed = RequiredNonNegative(entry, "baseSpeed", ContentLimits.MaxSpeed);
        double attackRange = RequiredPositive(entry, "attackRange", ContentLimits.MaxDistance);
        int attackIntervalMs = entry.RequiredInt("attackIntervalMs", 1, ContentLimits.MaxDurationMs);
        MonsterBehavior behavior = entry.RequiredEnum<MonsterBehavior>("behavior");
        double perceptionRadius = RequiredNonNegative(entry, "perceptionRadius", ContentLimits.MaxDistance);
        double leashRadius = RequiredPositive(entry, "leashRadius", ContentLimits.MaxDistance);
        double roamRadius = RequiredNonNegative(entry, "roamRadius", ContentLimits.MaxDistance);
        int idlePauseMinMs = entry.RequiredInt("idlePauseMinMs", 0, ContentLimits.MaxDurationMs);
        int idlePauseMaxMs = entry.RequiredInt("idlePauseMaxMs", 0, ContentLimits.MaxDurationMs);
        if (idlePauseMinMs > idlePauseMaxMs)
        {
            entry.Report("idlePauseMinMs", "must not be greater than idlePauseMaxMs");
        }

        int scanIntervalMs = entry.RequiredInt("scanIntervalMs", 1, ContentLimits.MaxDurationMs);
        double keepDistance = RequiredNonNegative(entry, "keepDistance", ContentLimits.MaxDistance);
        if (keepDistance > 0d && keepDistance >= attackRange)
        {
            entry.Report("keepDistance", "must be below attackRange");
        }

        bool assists = entry.RequiredBool("assist");
        double assistRadius = RequiredNonNegative(entry, "assistRadius", ContentLimits.MaxDistance);
        if (assists != assistRadius > 0d)
        {
            entry.Report("assistRadius", "must be above 0 for a monster that assists, and 0 otherwise");
        }

        int baseExperience = entry.RequiredInt("baseExperience", 0, ContentLimits.MaxExperience);
        int jobExperience = entry.RequiredInt("jobExperience", 0, ContentLimits.MaxExperience);

        var drops = new List<MonsterDrop>();
        foreach (PackageObjectReader drop in entry.RequiredObjectArray("drops"))
        {
            ItemDefinitionId item = drop.RequiredId<ItemDefinitionId>("item", ItemDefinitionId.TryCreate);
            double chance = drop.RequiredDouble("chance");
            if (chance < 0d || chance > 1d)
            {
                drop.Report("chance", "must be between 0 and 1");
            }

            int minAmount = drop.RequiredInt("minAmount", 1, ContentLimits.MaxStack);
            int maxAmount = drop.RequiredInt("maxAmount", 1, ContentLimits.MaxStack);
            if (minAmount > maxAmount)
            {
                drop.Report("minAmount", "must not be greater than maxAmount");
            }

            drop.ReportUnexpectedProperties();
            drops.Add(new MonsterDrop(item, chance, minAmount, maxAmount));
        }

        var skills = new List<MonsterSkill>();
        var listedSkills = new HashSet<SkillDefinitionId>();
        foreach (PackageObjectReader tried in entry.RequiredObjectArray("skills"))
        {
            SkillDefinitionId skill = tried.RequiredId<SkillDefinitionId>("skill", SkillDefinitionId.TryCreate);
            double chance = tried.RequiredDouble("chance");
            if (chance < 0d || chance > 1d)
            {
                tried.Report("chance", "must be between 0 and 1");
            }

            if (skill != default && !listedSkills.Add(skill))
            {
                tried.Report("skill", $"'{skill}' appears more than once");
            }

            tried.ReportUnexpectedProperties();
            skills.Add(new MonsterSkill(skill, chance));
        }

        entry.ReportUnexpectedProperties();
        if (problems.Count != problemsBefore)
        {
            return null;
        }

        return new MonsterDefinition(
            id,
            displayName,
            level,
            hp,
            physicalAttack,
            physicalDefense,
            hit,
            flee,
            magicAttack,
            baseSpeed,
            attackRange,
            attackIntervalMs,
            behavior,
            perceptionRadius,
            leashRadius,
            roamRadius,
            idlePauseMinMs,
            idlePauseMaxMs,
            scanIntervalMs,
            keepDistance,
            baseExperience,
            jobExperience,
            drops.AsReadOnly(),
            skills.AsReadOnly(),
            isBoss,
            assists,
            assistRadius);
    }

    private static SkillDefinition? ReadSkill(PackageObjectReader entry, SkillDefinitionId id, List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        SkillTargetType targetType = entry.RequiredEnum<SkillTargetType>("targetType");
        SkillDamageType? damageType = entry.Has("damageType")
            ? entry.RequiredEnum<SkillDamageType>("damageType")
            : null;
        double range = RequiredNonNegative(entry, "range", ContentLimits.MaxDistance);
        SkillPaymentPoint spPaidAt = entry.RequiredEnum<SkillPaymentPoint>("spPaidAt");
        int maxLevel = entry.RequiredInt("maxLevel", 0, ContentLimits.MaxSkillLevel);
        var levels = new List<SkillLevel>();
        foreach (PackageObjectReader level in entry.RequiredObjectArray("levels"))
        {
            SkillLevel? read = ReadSkillLevel(level);
            if (read != null)
            {
                levels.Add(read);
            }
        }

        if (problems.Count == problemsBefore && levels.Count != maxLevel)
        {
            entry.Report("levels", $"must list exactly maxLevel ({maxLevel}) levels");
        }

        SkillRequirement? requires = null;
        if (entry.Has("requires"))
        {
            PackageObjectReader? required = entry.RequiredObject("requires");
            if (required != null)
            {
                SkillDefinitionId skill = required.RequiredId<SkillDefinitionId>("skill", SkillDefinitionId.TryCreate);
                int level = required.RequiredInt("level", 1, ContentLimits.MaxSkillLevel);
                required.ReportUnexpectedProperties();
                requires = new SkillRequirement(skill, level);
            }
        }

        // Every level does the same kind of thing; only its strength differs (Gameplay Systems §9).
        SkillEffectKind? kind = levels.Count > 0 ? levels[0].Effect.Kind : null;
        if (levels.Any(level => level.Effect.Kind != kind))
        {
            entry.Report("levels", "every level must have the same kind of effect");
        }

        if (kind == SkillEffectKind.Damage && damageType == null)
        {
            entry.Report("damageType", "is required for a damage effect");
        }

        if (kind == SkillEffectKind.Status && targetType != SkillTargetType.Self)
        {
            entry.Report("targetType", "must be self for a status effect");
        }

        // A player's hit on itself has no hit context to resolve (CombatSystem).
        if (kind == SkillEffectKind.Damage && targetType != SkillTargetType.Enemy)
        {
            entry.Report("targetType", "must be enemy for a damage effect");
        }

        // Another player may only be healed: there is no PvP (Gameplay Systems §6, §9).
        if (targetType == SkillTargetType.Ally && kind != SkillEffectKind.Heal)
        {
            entry.Report("targetType", "may be ally only for a heal effect");
        }

        entry.ReportUnexpectedProperties();
        return problems.Count == problemsBefore
            ? new SkillDefinition(
                id,
                displayName,
                targetType,
                damageType,
                range,
                spPaidAt,
                levels.AsReadOnly(),
                requires)
            : null;
    }

    private static SkillLevel? ReadSkillLevel(PackageObjectReader level)
    {
        int spCost = level.RequiredInt("spCost", 0, ContentLimits.MaxHp);
        int fixedCastMs = level.RequiredInt("fixedCastMs", 0, ContentLimits.MaxDurationMs);
        int variableCastMs = level.RequiredInt("variableCastMs", 0, ContentLimits.MaxDurationMs);
        int afterCastDelayMs = level.RequiredInt("afterCastDelayMs", 0, ContentLimits.MaxDurationMs);
        int cooldownMs = level.RequiredInt("cooldownMs", 0, ContentLimits.MaxDurationMs);
        SkillEffect? effect = ReadEffect(level.RequiredObject("effect"));
        level.ReportUnexpectedProperties();
        return effect != null
            ? new SkillLevel(spCost, fixedCastMs, variableCastMs, afterCastDelayMs, cooldownMs, effect)
            : null;
    }

    private static SkillEffect? ReadEffect(PackageObjectReader? effect)
    {
        if (effect == null)
        {
            return null;
        }

        bool isDamage = effect.Has("damageRatio");
        bool isHeal = effect.Has("healHp");
        bool isStatus = effect.Has("status");
        SkillEffect? read = null;
        if ((isDamage ? 1 : 0) + (isHeal ? 1 : 0) + (isStatus ? 1 : 0) != 1)
        {
            effect.Report("damageRatio", "an effect must have exactly one of damageRatio, healHp, or status");
        }
        else if (isDamage)
        {
            int ratio = effect.RequiredInt("damageRatio", 1, ContentLimits.MaxDamageRatioPercent);
            read = ratio > 0 ? SkillEffect.Damage(ratio) : null;
        }
        else if (isStatus)
        {
            StatusDefinitionId status = effect.RequiredId<StatusDefinitionId>("status", StatusDefinitionId.TryCreate);
            int durationMs = effect.RequiredInt("durationMs", 1, ContentLimits.MaxDurationMs);
            StatPercentages percent = ReadStatPercent(effect.RequiredObject("statPercent"));
            read = status != default && durationMs > 0 ? SkillEffect.StatusEffect(status, durationMs, percent) : null;
        }
        else
        {
            int hp = effect.RequiredInt("healHp", 1, ContentLimits.MaxHp);
            read = hp > 0 ? SkillEffect.Heal(hp) : null;
        }

        effect.ReportUnexpectedProperties();
        return read;
    }

    private static StatPercentages ReadStatPercent(PackageObjectReader? percent)
    {
        int[] values = new int[6];
        if (percent != null)
        {
            string[] names = { "str", "agi", "vit", "int", "dex", "luk" };
            for (int index = 0; index < names.Length; index++)
            {
                values[index] = percent.RequiredInt(names[index], 0, ContentLimits.MaxStatPercent);
            }

            percent.ReportUnexpectedProperties();
        }

        return new StatPercentages(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    // A base job is complete as read; a first job keeps its own values until its base job is known (ResolveJobs).
    private static JobEntry? ReadJob(PackageObjectReader entry, JobDefinitionId id, List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        JobDefinitionId? baseJob = null;
        if (entry.Has("baseJob"))
        {
            baseJob = entry.RequiredId<JobDefinitionId>("baseJob", JobDefinitionId.TryCreate);
            foreach (string inherited in InheritedJobFields)
            {
                if (entry.Has(inherited))
                {
                    entry.Report(inherited, "is taken from the base job, so a first job omits it");
                }
            }
        }

        int healthBase = entry.RequiredInt("healthBase", 1, ContentLimits.MaxHp);
        int healthPerLevel = entry.RequiredInt("healthPerLevel", 0, ContentLimits.MaxHp);
        int spiritBase = entry.RequiredInt("spiritBase", 0, ContentLimits.MaxHp);
        int spiritPerLevel = entry.RequiredInt("spiritPerLevel", 0, ContentLimits.MaxHp);
        ExperienceDefinitionId jobExperienceTable = entry.RequiredId<ExperienceDefinitionId>(
            "jobExperienceTable",
            ExperienceDefinitionId.TryCreate);
        var skills = new List<SkillDefinitionId>();
        IReadOnlyList<string> skillTexts = entry.RequiredStringArray("skills");
        if (skillTexts.Count > SkillList.MaxEntries)
        {
            entry.Report("skills", $"lists more than the {SkillList.MaxEntries} skills a skill list carries");
        }

        foreach (string text in skillTexts)
        {
            if (!SkillDefinitionId.TryCreate(text, out SkillDefinitionId skill) || skills.Contains(skill))
            {
                entry.Report("skills", $"'{text}' is not a valid skill ID or appears more than once");
            }
            else
            {
                skills.Add(skill);
            }
        }

        List<WeaponType> weapons = ReadWeapons(entry);
        JobDefinition? definition = baseJob == null
            ? ReadBaseJob(
                entry,
                id,
                displayName,
                healthBase,
                healthPerLevel,
                spiritBase,
                spiritPerLevel,
                jobExperienceTable,
                skills,
                weapons)
            : null;
        entry.ReportUnexpectedProperties();

        if (problems.Count != problemsBefore)
        {
            return null;
        }

        return definition != null
            ? new JobEntry(definition)
            : new JobEntry(
                displayName,
                baseJob.GetValueOrDefault(),
                healthBase,
                healthPerLevel,
                spiritBase,
                spiritPerLevel,
                jobExperienceTable,
                skills.AsReadOnly(),
                weapons.AsReadOnly());
    }

    private static JobDefinition ReadBaseJob(
        PackageObjectReader entry,
        JobDefinitionId id,
        string displayName,
        int healthBase,
        int healthPerLevel,
        int spiritBase,
        int spiritPerLevel,
        ExperienceDefinitionId jobExperienceTable,
        List<SkillDefinitionId> skills,
        List<WeaponType> weapons)
    {
        int str = 0;
        int agi = 0;
        int vit = 0;
        int intelligence = 0;
        int dex = 0;
        int luk = 0;
        PackageObjectReader? stats = entry.RequiredObject("startingStats");
        if (stats != null)
        {
            str = stats.RequiredInt("str", 0, ContentLimits.MaxPrimaryStat);
            agi = stats.RequiredInt("agi", 0, ContentLimits.MaxPrimaryStat);
            vit = stats.RequiredInt("vit", 0, ContentLimits.MaxPrimaryStat);
            intelligence = stats.RequiredInt("int", 0, ContentLimits.MaxPrimaryStat);
            dex = stats.RequiredInt("dex", 0, ContentLimits.MaxPrimaryStat);
            luk = stats.RequiredInt("luk", 0, ContentLimits.MaxPrimaryStat);
            stats.ReportUnexpectedProperties();
        }

        int unarmedAttackSpeedPenalty = entry.RequiredInt(
            "unarmedAttackSpeedPenalty",
            0,
            ContentLimits.MaxAttackSpeedPenalty);
        double baseSpeed = RequiredPositive(entry, "baseSpeed", ContentLimits.MaxSpeed);
        MapDefinitionId startingMap = entry.RequiredId<MapDefinitionId>("startingMap", MapDefinitionId.TryCreate);
        SkillDefinitionId basicAttack = entry.RequiredId<SkillDefinitionId>("basicAttack", SkillDefinitionId.TryCreate);
        ExperienceDefinitionId experienceTable = entry.RequiredId<ExperienceDefinitionId>(
            "experienceTable",
            ExperienceDefinitionId.TryCreate);
        return new JobDefinition(
            id,
            displayName,
            new PrimaryStats(str, agi, vit, intelligence, dex, luk),
            healthBase,
            healthPerLevel,
            spiritBase,
            spiritPerLevel,
            unarmedAttackSpeedPenalty,
            baseSpeed,
            startingMap,
            basicAttack,
            experienceTable,
            jobExperienceTable,
            skills.AsReadOnly(),
            weapons.AsReadOnly());
    }

    // Every job wields at least one weapon type, each listed once (Gameplay Systems §11.1).
    private static List<WeaponType> ReadWeapons(PackageObjectReader entry)
    {
        var weapons = new List<WeaponType>();
        IReadOnlyList<string> texts = entry.RequiredStringArray("weapons");
        foreach (string text in texts)
        {
            WeaponType? weapon = null;
            foreach (WeaponType candidate in (WeaponType[])Enum.GetValues(typeof(WeaponType)))
            {
                string name = candidate.ToString();
                if (string.Equals($"{char.ToLowerInvariant(name[0])}{name.Substring(1)}", text,
                        StringComparison.Ordinal))
                {
                    weapon = candidate;
                }
            }

            if (weapon == null || weapons.Contains(weapon.Value))
            {
                entry.Report("weapons", $"'{text}' is not a weapon type or appears more than once");
            }
            else
            {
                weapons.Add(weapon.Value);
            }
        }

        if (texts.Count == 0 && entry.Has("weapons"))
        {
            entry.Report("weapons", "must list at least one weapon type");
        }

        return weapons;
    }

    // The second pass (Content Pipeline §4): a first job takes its base job's values, and its base job's job table
    // caps the points it carries. A base job must be a base job, and a first job's own skills must not repeat its base
    // tree; a base job or table refused for its own problem is reported there alone.
    private static Dictionary<JobDefinitionId, JobDefinition> ResolveJobs(
        Dictionary<JobDefinitionId, JobEntry> entries,
        IReadOnlyDictionary<ExperienceDefinitionId, ExperienceTableDefinition> experienceTables,
        HashSet<JobDefinitionId> declaredJobs,
        List<string> problems)
    {
        var jobs = new Dictionary<JobDefinitionId, JobDefinition>();
        foreach (KeyValuePair<JobDefinitionId, JobEntry> pair in entries)
        {
            if (pair.Value.Definition != null)
            {
                jobs.Add(pair.Key, pair.Value.Definition);
            }
        }

        foreach (KeyValuePair<JobDefinitionId, JobEntry> pair in entries)
        {
            JobEntry entry = pair.Value;
            if (entry.Definition != null)
            {
                continue;
            }

            if (!entries.TryGetValue(entry.BaseJob, out JobEntry? baseEntry))
            {
                if (!declaredJobs.Contains(entry.BaseJob))
                {
                    problems.Add($"{JobsFile}: {pair.Key}: its base job '{entry.BaseJob}' is unknown");
                }

                continue;
            }

            JobDefinition? baseJob = baseEntry.Definition;
            if (baseJob == null)
            {
                problems.Add($"{JobsFile}: {pair.Key}: its base job '{entry.BaseJob}' has a base job of its own");
                continue;
            }

            bool isRepeated = false;
            foreach (SkillDefinitionId skill in entry.Skills)
            {
                if (baseJob.Tree.Contains(skill))
                {
                    problems.Add($"{JobsFile}: {pair.Key}: skill '{skill}' repeats one of its base job's tree");
                    isRepeated = true;
                }
            }

            if (isRepeated
                || !experienceTables.TryGetValue(baseJob.JobExperienceTable, out ExperienceTableDefinition? table))
            {
                continue;
            }

            jobs.Add(
                pair.Key,
                JobDefinition.FirstJob(
                    pair.Key,
                    entry.DisplayName,
                    baseJob,
                    table,
                    entry.HealthBase,
                    entry.HealthPerLevel,
                    entry.SpiritBase,
                    entry.SpiritPerLevel,
                    entry.JobExperienceTable,
                    entry.Skills,
                    entry.Weapons));
        }

        return jobs;
    }

    private static NpcDefinition? ReadNpc(PackageObjectReader entry, NpcDefinitionId id, List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        var shop = new List<ShopEntry>();
        var listed = new HashSet<ItemDefinitionId>();
        foreach (PackageObjectReader stock in entry.RequiredObjectArray("shop"))
        {
            ItemDefinitionId item = stock.RequiredId<ItemDefinitionId>("item", ItemDefinitionId.TryCreate);
            int price = stock.RequiredInt("price", 1, ContentLimits.MaxPrice);
            stock.ReportUnexpectedProperties();
            if (item != default && !listed.Add(item))
            {
                stock.Report("item", $"'{item}' appears more than once");
            }

            shop.Add(new ShopEntry(item, price));
        }

        bool offersReset = entry.RequiredBool("reset");
        bool offersJobChange = entry.RequiredBool("jobChange");
        entry.ReportUnexpectedProperties();
        return problems.Count == problemsBefore
            ? new NpcDefinition(id, displayName, shop.AsReadOnly(), offersReset, offersJobChange)
            : null;
    }

    private static QuestDefinition? ReadQuest(PackageObjectReader entry, QuestDefinitionId id, List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        NpcDefinitionId giver = entry.RequiredId<NpcDefinitionId>("giver", NpcDefinitionId.TryCreate);
        MonsterDefinitionId monster = entry.RequiredId<MonsterDefinitionId>("monster", MonsterDefinitionId.TryCreate);
        int count = entry.RequiredInt("count", 1, ContentLimits.MaxKillCount);
        int baseExperience = entry.RequiredInt("baseExperience", 0, ContentLimits.MaxExperience);
        int jobExperience = entry.RequiredInt("jobExperience", 0, ContentLimits.MaxExperience);
        int currency = entry.RequiredInt("currency", 0, ContentLimits.MaxCurrency);
        entry.ReportUnexpectedProperties();
        if (problems.Count == problemsBefore && baseExperience == 0 && jobExperience == 0 && currency == 0)
        {
            entry.Report("baseExperience", "a quest must reward base experience, job experience, or coins");
        }

        return problems.Count == problemsBefore
            ? new QuestDefinition(id, displayName, giver, monster, count, baseExperience, jobExperience, currency)
            : null;
    }

    private static ExperienceTableDefinition? ReadExperienceTable(
        PackageObjectReader entry,
        ExperienceDefinitionId id,
        List<string> problems)
    {
        int problemsBefore = problems.Count;
        IReadOnlyList<int> levels = entry.RequiredIntArray("levels", 1, ContentLimits.MaxExperience);
        entry.ReportUnexpectedProperties();
        if (problems.Count == problemsBefore && levels.Count == 0)
        {
            entry.Report("levels", "must list at least one level");
        }
        else if (levels.Count > ContentLimits.MaxExperienceLevels)
        {
            entry.Report("levels", $"must list at most {ContentLimits.MaxExperienceLevels} levels");
        }

        return problems.Count == problemsBefore
            ? new ExperienceTableDefinition(id, new List<int>(levels).AsReadOnly())
            : null;
    }

    private static StatusEffectDefinition? ReadStatusEffect(
        PackageObjectReader entry,
        StatusDefinitionId id,
        List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        entry.ReportUnexpectedProperties();
        return problems.Count == problemsBefore ? new StatusEffectDefinition(id, displayName) : null;
    }

    private static MapDefinition? ReadMap(PackageObjectReader entry, MapDefinitionId id, List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");

        WorldPosition spawnPosition = default;
        WorldDirection spawnFacing = default;
        PackageObjectReader? spawnPoint = entry.RequiredObject("spawnPoint");
        if (spawnPoint != null)
        {
            spawnPosition = ReadPosition(spawnPoint.RequiredObject("position"));
            PackageObjectReader? facing = spawnPoint.RequiredObject("facing");
            if (facing != null)
            {
                spawnFacing = new WorldDirection(RequiredCoordinate(facing, "x"), RequiredCoordinate(facing, "z"));
                facing.ReportUnexpectedProperties();
                if (spawnFacing == default)
                {
                    spawnPoint.Report("facing", "must not be the zero direction");
                }
            }

            spawnPoint.ReportUnexpectedProperties();
        }

        var portals = new List<MapPortal>();
        foreach (PackageObjectReader portal in entry.RequiredObjectArray("portals"))
        {
            WorldPosition center = ReadPosition(portal.RequiredObject("center"));
            double radius = RequiredPositive(portal, "radius", ContentLimits.MaxDistance);
            PackageObjectReader? destination = portal.RequiredObject("destination");
            if (destination != null)
            {
                MapDefinitionId map = destination.RequiredId<MapDefinitionId>("map", MapDefinitionId.TryCreate);
                WorldPosition position = ReadPosition(destination.RequiredObject("position"));
                WorldDirection facing = default;
                PackageObjectReader? facingReader = destination.RequiredObject("facing");
                if (facingReader != null)
                {
                    facing = new WorldDirection(
                        RequiredCoordinate(facingReader, "x"),
                        RequiredCoordinate(facingReader, "z"));
                    facingReader.ReportUnexpectedProperties();
                }

                destination.ReportUnexpectedProperties();
                if (facing == default)
                {
                    destination.Report("facing", "must not be the zero direction");
                }
                else if (radius > 0d && map != default)
                {
                    portals.Add(new MapPortal(center, radius, map, position, facing));
                }
            }

            portal.ReportUnexpectedProperties();
        }

        var monsterSpawns = new List<MonsterSpawn>();
        foreach (PackageObjectReader spawn in entry.RequiredObjectArray("monsterSpawns"))
        {
            MonsterDefinitionId monster = spawn.RequiredId<MonsterDefinitionId>(
                "monster",
                MonsterDefinitionId.TryCreate);
            WorldPosition center = ReadPosition(spawn.RequiredObject("center"));
            double radius = RequiredNonNegative(spawn, "radius", ContentLimits.MaxDistance);
            int count = spawn.RequiredInt("count", 1, ContentLimits.MaxSpawnCount);
            int respawnMs = spawn.RequiredInt("respawnMs", 0, ContentLimits.MaxDurationMs);
            spawn.ReportUnexpectedProperties();
            monsterSpawns.Add(new MonsterSpawn(monster, center, radius, count, respawnMs));
        }

        var npcs = new List<NpcPlacement>();
        foreach (PackageObjectReader npc in entry.RequiredObjectArray("npcs"))
        {
            NpcDefinitionId placed = npc.RequiredId<NpcDefinitionId>("npc", NpcDefinitionId.TryCreate);
            WorldPosition position = ReadPosition(npc.RequiredObject("position"));
            WorldDirection facing = default;
            PackageObjectReader? facingReader = npc.RequiredObject("facing");
            if (facingReader != null)
            {
                facing = new WorldDirection(
                    RequiredCoordinate(facingReader, "x"),
                    RequiredCoordinate(facingReader, "z"));
                facingReader.ReportUnexpectedProperties();
            }

            npc.ReportUnexpectedProperties();
            if (facing == default)
            {
                npc.Report("facing", "must not be the zero direction");
            }
            else if (placed != default)
            {
                npcs.Add(new NpcPlacement(placed, position, facing));
            }
        }

        NavigationGrid? navigation = NavigationPackageReader.Read(entry, problems);
        entry.ReportUnexpectedProperties();
        if (problems.Count != problemsBefore || navigation == null)
        {
            return null;
        }

        // Players are placed here on entry, so a spawn point the grid rejects would strand every new session.
        if (!navigation.CanOccupy(spawnPosition.X, spawnPosition.Z))
        {
            spawnPoint!.Report("position", "is not a place the navigation grid lets an agent stand");
            return null;
        }

        var definition = new MapDefinition(
            id,
            displayName,
            spawnPosition,
            spawnFacing,
            monsterSpawns.AsReadOnly(),
            navigation,
            portals.AsReadOnly(),
            npcs.AsReadOnly());
        if (definition.IsInPortal(spawnPosition))
        {
            spawnPoint!.Report("position", "lies inside a portal");
            return null;
        }

        return CheckNpcPlacements(definition, problems) ? definition : null;
    }

    // Each NPC stands on a marker cell of its own at the marker's height. Whether a player can walk up to it is the
    // tools' check, as a portal's reachability is.
    private static bool CheckNpcPlacements(MapDefinition map, List<string> problems)
    {
        int problemsBefore = problems.Count;
        NavigationGrid grid = map.Navigation;
        var markers = new HashSet<(int Column, int Row)>();
        foreach (NpcPlacement npc in map.Npcs)
        {
            if (!grid.TryGetCellIndex(npc.Position.X, npc.Position.Z, out int column, out int row)
                || grid.GetCell(column, row).Surface != NavigationSurface.NpcMarker)
            {
                problems.Add($"{MapsFile}: {map.Id}: NPC '{npc.Npc}' does not stand on an NPC marker cell");
            }
            else if (Math.Abs(npc.Position.Y - grid.GetCell(column, row).HeightAtMin) > GroundHeightTolerance)
            {
                problems.Add($"{MapsFile}: {map.Id}: NPC '{npc.Npc}' does not stand at its marker's height");
            }
            else if (!markers.Add((column, row)))
            {
                problems.Add($"{MapsFile}: {map.Id}: NPC '{npc.Npc}' stands on a marker cell another NPC stands on");
            }
        }

        return problems.Count == problemsBefore;
    }

    private static WorldPosition ReadPosition(PackageObjectReader? position)
    {
        if (position == null)
        {
            return default;
        }

        var result = new WorldPosition(
            RequiredCoordinate(position, "x"),
            RequiredCoordinate(position, "y"),
            RequiredCoordinate(position, "z"));
        position.ReportUnexpectedProperties();
        return result;
    }

    // The bounds below are the tools', so a hand-edited package cannot carry a value the tools refuse (finding 5 of
    // the Milestone 6 review).
    private static float RequiredCoordinate(PackageObjectReader reader, string name)
    {
        double value = reader.RequiredDouble(name);
        if (Math.Abs(value) > ContentLimits.MaxCoordinate)
        {
            reader.Report(name, $"must be between -{ContentLimits.MaxCoordinate} and {ContentLimits.MaxCoordinate}");
        }

        // Kept within the bounds, so a direction out of range is not also reported as the zero direction.
        return (float)Math.Max(-ContentLimits.MaxCoordinate, Math.Min(ContentLimits.MaxCoordinate, value));
    }

    private static double RequiredNonNegative(PackageObjectReader reader, string name, double maximum)
    {
        double value = reader.RequiredDouble(name);
        if (value < 0d)
        {
            reader.Report(name, "must not be negative");
        }
        else if (value > maximum)
        {
            reader.Report(name, $"must be at most {maximum}");
        }

        return value;
    }

    private static double RequiredPositive(PackageObjectReader reader, string name, double maximum)
    {
        double value = reader.RequiredDouble(name);
        if (!(value > 0d))
        {
            reader.Report(name, "must be greater than 0");
        }
        else if (value > maximum)
        {
            reader.Report(name, $"must be at most {maximum}");
        }

        return value;
    }

    private static void CheckReferences(
        IEnumerable<MonsterDefinition> monsters,
        IEnumerable<JobDefinition> jobs,
        IEnumerable<MapDefinition> maps,
        IReadOnlyDictionary<SkillDefinitionId, SkillDefinition> skills,
        IReadOnlyDictionary<ItemDefinitionId, ItemDefinition> items,
        HashSet<ItemDefinitionId> declaredItems,
        HashSet<MonsterDefinitionId> declaredMonsters,
        HashSet<SkillDefinitionId> declaredSkills,
        HashSet<MapDefinitionId> declaredMaps,
        HashSet<ExperienceDefinitionId> declaredExperienceTables,
        HashSet<StatusDefinitionId> declaredStatusEffects,
        List<string> problems)
    {
        foreach (SkillDefinition skill in skills.Values)
        {
            IEnumerable<StatusDefinitionId> applied = skill.Levels
                .Select(level => level.Effect)
                .Where(effect => effect.Kind == SkillEffectKind.Status)
                .Select(effect => effect.Status)
                .Distinct();
            foreach (StatusDefinitionId status in applied)
            {
                if (!declaredStatusEffects.Contains(status))
                {
                    problems.Add($"{SkillsFile}: {skill.Id}: applies unknown status effect '{status}'");
                }
            }
        }

        foreach (MonsterDefinition monster in monsters)
        {
            foreach (MonsterDrop drop in monster.Drops)
            {
                if (!declaredItems.Contains(drop.Item))
                {
                    problems.Add($"{MonstersFile}: {monster.Id}: drops unknown item '{drop.Item}'");
                }
                else if (items.TryGetValue(drop.Item, out ItemDefinition? item) && drop.MaxAmount > item.StackLimit)
                {
                    // A pickup is all or nothing (Gameplay Systems §11), so a larger drop could never be picked up.
                    problems.Add(
                        $"{MonstersFile}: {monster.Id}: drops up to {drop.MaxAmount} of '{drop.Item}', more than its "
                        + $"stack limit of {item.StackLimit}");
                }
            }

            foreach (MonsterSkill tried in monster.Skills)
            {
                if (!declaredSkills.Contains(tried.Skill))
                {
                    problems.Add($"{MonstersFile}: {monster.Id}: casts unknown skill '{tried.Skill}'");
                }
                else if (skills.TryGetValue(tried.Skill, out SkillDefinition? known) && !known.HasEffect)
                {
                    problems.Add($"{MonstersFile}: {monster.Id}: casts skill '{tried.Skill}', which has no effect");
                }
                else if (known != null && known.TargetType != SkillTargetType.Enemy)
                {
                    // A monster casts at the player it fights, so a skill for its caster would land on that player.
                    problems.Add(
                        $"{MonstersFile}: {monster.Id}: casts skill '{tried.Skill}', which is not cast at an enemy");
                }
            }
        }

        var mapsById = new Dictionary<MapDefinitionId, MapDefinition>();
        foreach (MapDefinition map in maps)
        {
            mapsById[map.Id] = map;
        }

        foreach (MapDefinition map in maps)
        {
            foreach (MonsterSpawn spawn in map.MonsterSpawns)
            {
                if (!declaredMonsters.Contains(spawn.Monster))
                {
                    problems.Add($"{MapsFile}: {map.Id}: spawns unknown monster '{spawn.Monster}'");
                }
            }

            CheckPortalDestinations(map, declaredMaps, mapsById, problems);
        }

        foreach (JobDefinition job in jobs)
        {
            if (job.BaseJob == null)
            {
                CheckBaseJobReferences(job, skills, declaredSkills, declaredMaps, declaredExperienceTables, problems);
            }
            else if (job.Tree.Count > SkillList.MaxEntries)
            {
                problems.Add(
                    $"{JobsFile}: {job.Id}: its whole tree holds {job.Tree.Count} skills, more than the "
                    + $"{SkillList.MaxEntries} a skill list carries");
            }

            if (!declaredExperienceTables.Contains(job.JobExperienceTable))
            {
                problems.Add($"{JobsFile}: {job.Id}: uses unknown job experience table '{job.JobExperienceTable}'");
            }

            foreach (SkillDefinitionId skill in job.Skills)
            {
                if (!declaredSkills.Contains(skill))
                {
                    problems.Add($"{JobsFile}: {job.Id}: knows unknown skill '{skill}'");
                }
                else if (skills.TryGetValue(skill, out SkillDefinition? known) && !known.HasEffect)
                {
                    problems.Add($"{JobsFile}: {job.Id}: knows skill '{skill}', which has no effect");
                }
            }

            CheckPrerequisites(job, skills, problems);
        }
    }

    private static void CheckBaseJobReferences(
        JobDefinition job,
        IReadOnlyDictionary<SkillDefinitionId, SkillDefinition> skills,
        HashSet<SkillDefinitionId> declaredSkills,
        HashSet<MapDefinitionId> declaredMaps,
        HashSet<ExperienceDefinitionId> declaredExperienceTables,
        List<string> problems)
    {
        if (!declaredMaps.Contains(job.StartingMap))
        {
            problems.Add($"{JobsFile}: {job.Id}: starts on unknown map '{job.StartingMap}'");
        }

        if (!declaredSkills.Contains(job.BasicAttack))
        {
            problems.Add($"{JobsFile}: {job.Id}: uses unknown skill '{job.BasicAttack}'");
        }

        if (!declaredExperienceTables.Contains(job.ExperienceTable))
        {
            problems.Add($"{JobsFile}: {job.Id}: uses unknown experience table '{job.ExperienceTable}'");
        }

        if (skills.TryGetValue(job.BasicAttack, out SkillDefinition? basicAttack) && basicAttack.DamageType == null)
        {
            problems.Add($"{JobsFile}: {job.Id}: its basic attack '{job.BasicAttack}' has no damage type");
        }
    }

    // A prerequisite is another skill of the job's whole tree at one of its levels, and prerequisites form no cycle
    // (Content Pipeline §4).
    private static void CheckPrerequisites(
        JobDefinition job,
        IReadOnlyDictionary<SkillDefinitionId, SkillDefinition> skills,
        List<string> problems)
    {
        foreach (SkillDefinitionId id in job.Skills)
        {
            if (!skills.TryGetValue(id, out SkillDefinition? skill) || skill.Requires == null)
            {
                continue;
            }

            // A required skill refused for its own problem is reported there alone.
            SkillRequirement requires = skill.Requires;
            if (requires.Skill == id || !job.Tree.Contains(requires.Skill))
            {
                problems.Add(
                    $"{JobsFile}: {job.Id}: skill '{id}' requires '{requires.Skill}', not another of its tree");
            }
            else if (skills.TryGetValue(requires.Skill, out SkillDefinition? required)
                     && requires.Level > required.MaxLevel)
            {
                problems.Add(
                    $"{JobsFile}: {job.Id}: skill '{id}' requires '{requires.Skill}' at level {requires.Level}, above "
                    + $"its maximum {required.MaxLevel}");
            }

            var seen = new HashSet<SkillDefinitionId> { id };
            SkillRequirement? next = requires;
            while (next != null && skills.TryGetValue(next.Skill, out SkillDefinition? step))
            {
                if (!seen.Add(next.Skill))
                {
                    problems.Add($"{JobsFile}: {job.Id}: skill '{id}' is in a cycle of prerequisites");
                    break;
                }

                next = step.Requires;
            }
        }
    }

    // Each NPC stands in one place in the world; a shop never sells below what it pays, so no buy and sale can gain
    // (Gameplay Systems §11.3); an NPC's services fit one message; a quest's giver stands somewhere.
    private static void CheckNpcsAndQuests(
        IEnumerable<NpcDefinition> npcs,
        IEnumerable<QuestDefinition> quests,
        IEnumerable<MapDefinition> maps,
        int firstJobs,
        IReadOnlyDictionary<ItemDefinitionId, ItemDefinition> items,
        HashSet<ItemDefinitionId> declaredItems,
        HashSet<MonsterDefinitionId> declaredMonsters,
        HashSet<NpcDefinitionId> declaredNpcs,
        bool isEveryMapRead,
        List<string> problems)
    {
        var placedOn = new Dictionary<NpcDefinitionId, MapDefinitionId>();
        foreach (MapDefinition map in maps)
        {
            foreach (NpcPlacement npc in map.Npcs)
            {
                if (!declaredNpcs.Contains(npc.Npc))
                {
                    problems.Add($"{MapsFile}: {map.Id}: places unknown NPC '{npc.Npc}'");
                }
                else if (placedOn.TryGetValue(npc.Npc, out MapDefinitionId first))
                {
                    problems.Add($"{MapsFile}: {map.Id}: places NPC '{npc.Npc}', which '{first}' already places");
                }
                else
                {
                    placedOn.Add(npc.Npc, map.Id);
                }
            }
        }

        var questList = quests.ToList();
        foreach (NpcDefinition npc in npcs)
        {
            var traded = new HashSet<ItemDefinitionId>();
            foreach (ShopEntry entry in npc.Shop)
            {
                traded.Add(entry.Item);
                if (!declaredItems.Contains(entry.Item))
                {
                    problems.Add($"{NpcsFile}: {npc.Id}: sells unknown item '{entry.Item}'");
                }
                else if (items.TryGetValue(entry.Item, out ItemDefinition? item) && entry.Price < item.SellPrice)
                {
                    problems.Add(
                        $"{NpcsFile}: {npc.Id}: sells '{entry.Item}' for {entry.Price}, below its sell price of "
                        + $"{item.SellPrice}");
                }
            }

            if (npc.HasShop)
            {
                traded.UnionWith(items.Values.Where(item => item.SellPrice > 0).Select(item => item.Id));
            }

            int offers = questList.Count(quest => quest.Giver == npc.Id);
            int size = ServicesHeaderBytes
                + ServicesEntryBytes * traded.Count
                + ServicesOfferBytes * offers
                + (npc.OffersJobChange ? ServicesJobChangeBytes * firstJobs : 0);
            if (size > MaxServicesBytes)
            {
                problems.Add(
                    $"{NpcsFile}: {npc.Id}: its services take {size} bytes, more than the {MaxServicesBytes} one "
                    + "message carries");
            }
        }

        foreach (QuestDefinition quest in questList)
        {
            if (!declaredNpcs.Contains(quest.Giver))
            {
                problems.Add($"{QuestsFile}: {quest.Id}: is given by unknown NPC '{quest.Giver}'");
            }
            else if (isEveryMapRead && !placedOn.ContainsKey(quest.Giver))
            {
                problems.Add($"{QuestsFile}: {quest.Id}: is given by NPC '{quest.Giver}', which no map places");
            }

            if (!declaredMonsters.Contains(quest.Monster))
            {
                problems.Add($"{QuestsFile}: {quest.Id}: asks for unknown monster '{quest.Monster}'");
            }
        }
    }

    // Reachability is the tools' check; the loader still refuses an arrival it could not place (Gameplay Systems §4.2).
    private static void CheckPortalDestinations(
        MapDefinition map,
        HashSet<MapDefinitionId> declaredMaps,
        Dictionary<MapDefinitionId, MapDefinition> mapsById,
        List<string> problems)
    {
        foreach (MapPortal portal in map.Portals)
        {
            if (!declaredMaps.Contains(portal.DestinationMap))
            {
                problems.Add($"{MapsFile}: {map.Id}: a portal leads to unknown map '{portal.DestinationMap}'");
                continue;
            }

            if (!mapsById.TryGetValue(portal.DestinationMap, out MapDefinition? destination))
            {
                continue;
            }

            WorldPosition at = portal.DestinationPosition;
            if (!destination.Navigation.CanOccupy(at.X, at.Z))
            {
                problems.Add(
                    $"{MapsFile}: {map.Id}: a portal's arrival on '{destination.Id}' is not a place to stand");
            }
            else if (destination.IsInPortal(at))
            {
                problems.Add($"{MapsFile}: {map.Id}: a portal's arrival lies inside a portal of '{destination.Id}'");
            }
        }
    }

    private static bool IsContentVersion(string value)
    {
        if (value.Length != ContentVersionLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool isDigit = character >= '0' && character <= '9';
            bool isLowerHex = character >= 'a' && character <= 'f';
            if (!isDigit && !isLowerHex)
            {
                return false;
            }
        }

        return true;
    }

    private static string ComputeHash(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    // Must stay in step with the tools: the first 16 hex digits of the SHA-256 over the sorted "path:hash" lines.
    private static string ComputeVersion(IReadOnlyDictionary<string, string> hashes)
    {
        var listing = new StringBuilder();
        foreach (KeyValuePair<string, string> entry in hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            listing.Append(entry.Key).Append(':').Append(entry.Value).Append('\n');
        }

        return ComputeHash(Encoding.UTF8.GetBytes(listing.ToString())).Substring(0, ContentVersionLength);
    }

    // A job as read: a base job's definition, or a first job's own values until its base job is known.
    private sealed class JobEntry
    {
        public JobEntry(JobDefinition definition)
        {
            Definition = definition;
            DisplayName = definition.DisplayName;
            Skills = definition.Skills;
            Weapons = definition.Weapons;
        }

        public JobEntry(
            string displayName,
            JobDefinitionId baseJob,
            int healthBase,
            int healthPerLevel,
            int spiritBase,
            int spiritPerLevel,
            ExperienceDefinitionId jobExperienceTable,
            IReadOnlyList<SkillDefinitionId> skills,
            IReadOnlyList<WeaponType> weapons)
        {
            DisplayName = displayName;
            BaseJob = baseJob;
            HealthBase = healthBase;
            HealthPerLevel = healthPerLevel;
            SpiritBase = spiritBase;
            SpiritPerLevel = spiritPerLevel;
            JobExperienceTable = jobExperienceTable;
            Skills = skills;
            Weapons = weapons;
        }

        public JobDefinition? Definition { get; }

        public string DisplayName { get; }

        public JobDefinitionId BaseJob { get; }

        public int HealthBase { get; }

        public int HealthPerLevel { get; }

        public int SpiritBase { get; }

        public int SpiritPerLevel { get; }

        public ExperienceDefinitionId JobExperienceTable { get; }

        public IReadOnlyList<SkillDefinitionId> Skills { get; }

        public IReadOnlyList<WeaponType> Weapons { get; }
    }

    private sealed class PackageManifest
    {
        public PackageManifest(
            string serverContentVersion,
            string clientContentVersion,
            IReadOnlyDictionary<string, string> hashes)
        {
            ServerContentVersion = serverContentVersion;
            ClientContentVersion = clientContentVersion;
            Hashes = hashes;
        }

        public string ServerContentVersion { get; }

        public string ClientContentVersion { get; }

        public IReadOnlyDictionary<string, string> Hashes { get; }
    }
}
}
