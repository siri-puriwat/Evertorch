using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Evertorch.Game;

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

    private const string ItemsFile = "items.json";
    private const string JobsFile = "jobs.json";
    private const string MapsFile = "maps.json";
    private const string MonstersFile = "monsters.json";
    private const string SkillsFile = "skills.json";
    private const int ContentVersionLength = 16;

    private static readonly string[] DataFiles = { ItemsFile, JobsFile, MapsFile, MonstersFile, SkillsFile };

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
        var jobs = new Dictionary<JobDefinitionId, JobDefinition>();
        var maps = new Dictionary<MapDefinitionId, MapDefinition>();

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
        ReadDefinitions<JobDefinitionId, JobDefinition>(
            files,
            JobsFile,
            problems,
            jobs,
            JobDefinitionId.TryCreate,
            ReadJob);
        HashSet<MapDefinitionId> declaredMaps = ReadDefinitions(
            files,
            MapsFile,
            problems,
            maps,
            MapDefinitionId.TryCreate,
            ReadMap);

        CheckReferences(
            monsters.Values,
            jobs.Values,
            maps.Values,
            declaredItems,
            declaredMonsters,
            declaredSkills,
            declaredMaps,
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
            maps);
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
        int stackLimit = entry.RequiredInt("stackLimit", 1);
        int weight = entry.RequiredInt("weight", 0);
        int sellPrice = entry.RequiredInt("sellPrice", 0);
        entry.ReportUnexpectedProperties();

        return problems.Count == problemsBefore
            ? new ItemDefinition(id, displayName, type, stackLimit, weight, sellPrice)
            : null;
    }

    private static MonsterDefinition? ReadMonster(
        PackageObjectReader entry,
        MonsterDefinitionId id,
        List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        int level = entry.RequiredInt("level", 1);
        int hp = entry.RequiredInt("hp", 1);
        int physicalAttack = entry.RequiredInt("physicalAttack", 0);
        int physicalDefense = entry.RequiredInt("physicalDefense", 0);
        int hit = entry.RequiredInt("hit", 0);
        int flee = entry.RequiredInt("flee", 0);
        double baseSpeed = RequiredNonNegative(entry, "baseSpeed");
        double attackRange = RequiredNonNegative(entry, "attackRange");
        int attackIntervalMs = entry.RequiredInt("attackIntervalMs", 1);
        MonsterBehavior behavior = entry.RequiredEnum<MonsterBehavior>("behavior");
        double perceptionRadius = RequiredNonNegative(entry, "perceptionRadius");
        double leashRadius = RequiredNonNegative(entry, "leashRadius");
        double roamRadius = RequiredNonNegative(entry, "roamRadius");
        int idlePauseMinMs = entry.RequiredInt("idlePauseMinMs", 0);
        int idlePauseMaxMs = entry.RequiredInt("idlePauseMaxMs", 0);
        if (idlePauseMinMs > idlePauseMaxMs)
        {
            entry.Report("idlePauseMinMs", "must not be greater than idlePauseMaxMs");
        }

        int scanIntervalMs = entry.RequiredInt("scanIntervalMs", 1);

        var drops = new List<MonsterDrop>();
        foreach (PackageObjectReader drop in entry.RequiredObjectArray("drops"))
        {
            ItemDefinitionId item = drop.RequiredId<ItemDefinitionId>("item", ItemDefinitionId.TryCreate);
            double chance = drop.RequiredDouble("chance");
            if (chance < 0d || chance > 1d)
            {
                drop.Report("chance", "must be between 0 and 1");
            }

            int minAmount = drop.RequiredInt("minAmount", 1);
            int maxAmount = drop.RequiredInt("maxAmount", 1);
            if (minAmount > maxAmount)
            {
                drop.Report("minAmount", "must not be greater than maxAmount");
            }

            drop.ReportUnexpectedProperties();
            drops.Add(new MonsterDrop(item, chance, minAmount, maxAmount));
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
            drops.AsReadOnly());
    }

    private static SkillDefinition? ReadSkill(PackageObjectReader entry, SkillDefinitionId id, List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");
        SkillTargetType targetType = entry.RequiredEnum<SkillTargetType>("targetType");
        SkillDamageType damageType = entry.RequiredEnum<SkillDamageType>("damageType");
        double range = RequiredNonNegative(entry, "range");
        entry.ReportUnexpectedProperties();

        return problems.Count == problemsBefore
            ? new SkillDefinition(id, displayName, targetType, damageType, range)
            : null;
    }

    private static JobDefinition? ReadJob(PackageObjectReader entry, JobDefinitionId id, List<string> problems)
    {
        int problemsBefore = problems.Count;
        string displayName = entry.RequiredString("displayName");

        int str = 0;
        int agi = 0;
        int vit = 0;
        int intelligence = 0;
        int dex = 0;
        int luk = 0;
        PackageObjectReader? stats = entry.RequiredObject("startingStats");
        if (stats != null)
        {
            str = stats.RequiredInt("str", 0);
            agi = stats.RequiredInt("agi", 0);
            vit = stats.RequiredInt("vit", 0);
            intelligence = stats.RequiredInt("int", 0);
            dex = stats.RequiredInt("dex", 0);
            luk = stats.RequiredInt("luk", 0);
            stats.ReportUnexpectedProperties();
        }

        int healthBase = entry.RequiredInt("healthBase", 1);
        int healthPerLevel = entry.RequiredInt("healthPerLevel", 0);
        int spiritBase = entry.RequiredInt("spiritBase", 0);
        int spiritPerLevel = entry.RequiredInt("spiritPerLevel", 0);
        int unarmedAttackSpeedPenalty = entry.RequiredInt("unarmedAttackSpeedPenalty", 0);
        double baseSpeed = RequiredNonNegative(entry, "baseSpeed");
        MapDefinitionId startingMap = entry.RequiredId<MapDefinitionId>("startingMap", MapDefinitionId.TryCreate);
        SkillDefinitionId basicAttack = entry.RequiredId<SkillDefinitionId>("basicAttack", SkillDefinitionId.TryCreate);
        entry.ReportUnexpectedProperties();

        if (problems.Count != problemsBefore)
        {
            return null;
        }

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
            basicAttack);
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
                spawnFacing = new WorldDirection((float)facing.RequiredDouble("x"), (float)facing.RequiredDouble("z"));
                facing.ReportUnexpectedProperties();
                if (spawnFacing == default)
                {
                    spawnPoint.Report("facing", "must not be the zero direction");
                }
            }

            spawnPoint.ReportUnexpectedProperties();
        }

        var monsterSpawns = new List<MonsterSpawn>();
        foreach (PackageObjectReader spawn in entry.RequiredObjectArray("monsterSpawns"))
        {
            MonsterDefinitionId monster = spawn.RequiredId<MonsterDefinitionId>(
                "monster",
                MonsterDefinitionId.TryCreate);
            WorldPosition center = ReadPosition(spawn.RequiredObject("center"));
            double radius = RequiredNonNegative(spawn, "radius");
            int count = spawn.RequiredInt("count", 1);
            int respawnMs = spawn.RequiredInt("respawnMs", 0);
            spawn.ReportUnexpectedProperties();
            monsterSpawns.Add(new MonsterSpawn(monster, center, radius, count, respawnMs));
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

        return new MapDefinition(
            id,
            displayName,
            spawnPosition,
            spawnFacing,
            monsterSpawns.AsReadOnly(),
            navigation);
    }

    private static WorldPosition ReadPosition(PackageObjectReader? position)
    {
        if (position == null)
        {
            return default;
        }

        var result = new WorldPosition(
            (float)position.RequiredDouble("x"),
            (float)position.RequiredDouble("y"),
            (float)position.RequiredDouble("z"));
        position.ReportUnexpectedProperties();
        return result;
    }

    private static double RequiredNonNegative(PackageObjectReader reader, string name)
    {
        double value = reader.RequiredDouble(name);
        if (value < 0d)
        {
            reader.Report(name, "must not be negative");
        }

        return value;
    }

    private static void CheckReferences(
        IEnumerable<MonsterDefinition> monsters,
        IEnumerable<JobDefinition> jobs,
        IEnumerable<MapDefinition> maps,
        HashSet<ItemDefinitionId> declaredItems,
        HashSet<MonsterDefinitionId> declaredMonsters,
        HashSet<SkillDefinitionId> declaredSkills,
        HashSet<MapDefinitionId> declaredMaps,
        List<string> problems)
    {
        foreach (MonsterDefinition monster in monsters)
        {
            foreach (MonsterDrop drop in monster.Drops)
            {
                if (!declaredItems.Contains(drop.Item))
                {
                    problems.Add($"{MonstersFile}: {monster.Id}: drops unknown item '{drop.Item}'");
                }
            }
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
        }

        foreach (JobDefinition job in jobs)
        {
            if (!declaredMaps.Contains(job.StartingMap))
            {
                problems.Add($"{JobsFile}: {job.Id}: starts on unknown map '{job.StartingMap}'");
            }

            if (!declaredSkills.Contains(job.BasicAttack))
            {
                problems.Add($"{JobsFile}: {job.Id}: uses unknown skill '{job.BasicAttack}'");
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
