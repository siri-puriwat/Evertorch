using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     Builds <see cref="ClientContent" /> from the bytes of a client content package. The package is generated, but it
///     reaches the client through a folder anyone can edit, so every file is checked against the manifest before use.
/// </summary>
public static class ClientContentParser
{
    public const string ManifestFile = "manifest.json";
    public const string MapsFile = "maps.json";
    public const string JobsFile = "jobs.json";
    public const string MonstersFile = "monsters.json";
    public const string ItemsFile = "items.json";
    public const string SkillsFile = "skills.json";
    public const string StatusEffectsFile = "status-effects.json";

    private const int SupportedSchemaVersion = 1;

    /// <summary>
    ///     The files a manifest lists, or an empty list with <paramref name="error" /> set.
    /// </summary>
    public static IReadOnlyList<string> ReadFileList(byte[] manifestBytes, out string error)
    {
        ManifestDto? manifest = ParseManifest(manifestBytes, out error);
        return manifest == null
            ? Array.Empty<string>()
            : manifest.files.Select(file => file.path).ToArray();
    }

    public static ClientContent? Parse(
        byte[] manifestBytes,
        IReadOnlyDictionary<string, byte[]> files,
        out string error)
    {
        ManifestDto? manifest = ParseManifest(manifestBytes, out error);
        if (manifest == null)
        {
            return null;
        }

        var listing = new StringBuilder();
        foreach (ManifestFileDto entry in manifest.files.OrderBy(file => file.path, StringComparer.Ordinal))
        {
            if (!files.TryGetValue(entry.path, out byte[]? content))
            {
                error = $"Content file '{entry.path}' is missing.";
                return null;
            }

            string hash = ComputeHash(content);
            if (!string.Equals(hash, entry.sha256, StringComparison.Ordinal))
            {
                error = $"Content file '{entry.path}' does not match the manifest.";
                return null;
            }

            listing.Append(entry.path).Append(':').Append(hash).Append('\n');
        }

        string version = ComputeHash(Encoding.UTF8.GetBytes(listing.ToString())).Substring(0, 16);
        if (!string.Equals(version, manifest.clientContentVersion, StringComparison.Ordinal))
        {
            error = "The manifest's content version does not match its files.";
            return null;
        }

        if (!TryGetListedFile(manifest, files, MapsFile, out byte[] mapsBytes, out error)
            || !TryGetListedFile(manifest, files, JobsFile, out byte[] jobsBytes, out error)
            || !TryGetListedFile(manifest, files, MonstersFile, out byte[] monstersBytes, out error)
            || !TryGetListedFile(manifest, files, ItemsFile, out byte[] itemsBytes, out error)
            || !TryGetListedFile(manifest, files, SkillsFile, out byte[] skillsBytes, out error)
            || !TryGetListedFile(manifest, files, StatusEffectsFile, out byte[] statusBytes, out error))
        {
            return null;
        }

        Dictionary<MapDefinitionId, ClientMap>? maps = ParseMaps(mapsBytes, out error);
        if (maps == null)
        {
            return null;
        }

        Dictionary<JobDefinitionId, ClientJob>? jobs = ParseJobs(jobsBytes, out error);
        if (jobs == null)
        {
            return null;
        }

        Dictionary<MonsterDefinitionId, ClientMonster>? monsters = ParseMonsters(monstersBytes, out error);
        if (monsters == null)
        {
            return null;
        }

        Dictionary<ItemDefinitionId, ClientItem>? items = ParseItems(itemsBytes, out error);
        if (items == null)
        {
            return null;
        }

        Dictionary<SkillDefinitionId, ClientSkill>? skills = ParseSkills(skillsBytes, out error);
        if (skills == null)
        {
            return null;
        }

        Dictionary<StatusDefinitionId, ClientStatusEffect>? statusEffects = ParseStatusEffects(statusBytes, out error);
        return statusEffects == null
            ? null
            : new ClientContent(version, maps, jobs, monsters, items, skills, statusEffects);
    }

    private static bool TryGetListedFile(
        ManifestDto manifest,
        IReadOnlyDictionary<string, byte[]> files,
        string name,
        out byte[] content,
        out string error)
    {
        if (!files.TryGetValue(name, out byte[]? found) || manifest.files.All(file => file.path != name))
        {
            content = Array.Empty<byte>();
            error = $"The content package has no '{name}'.";
            return false;
        }

        content = found;
        error = string.Empty;
        return true;
    }

    private static ManifestDto? ParseManifest(byte[] manifestBytes, out string error)
    {
        error = string.Empty;
        ManifestDto? manifest = FromJson<ManifestDto>(manifestBytes);
        if (manifest == null || manifest.files == null || manifest.files.Length == 0)
        {
            error = "The content manifest is not readable.";
            return null;
        }

        if (manifest.schemaVersion != SupportedSchemaVersion)
        {
            error = $"The content manifest has schema version {manifest.schemaVersion}; expected 1.";
            return null;
        }

        foreach (ManifestFileDto file in manifest.files)
        {
            bool isPlainName = !string.IsNullOrEmpty(file.path)
                && file.path.IndexOfAny(new[] { '/', '\\', ':' }) < 0
                && file.path != ManifestFile;
            if (!isPlainName)
            {
                error = "The content manifest lists an unusable file name.";
                return null;
            }
        }

        return manifest;
    }

    private static Dictionary<MapDefinitionId, ClientMap>? ParseMaps(byte[] mapsBytes, out string error)
    {
        error = string.Empty;
        MapsDto? dto = FromJson<MapsDto>(mapsBytes);
        if (dto == null || dto.definitions == null || dto.schemaVersion != SupportedSchemaVersion)
        {
            error = $"'{MapsFile}' is not readable or has an unsupported schema version.";
            return null;
        }

        var maps = new Dictionary<MapDefinitionId, ClientMap>();
        foreach (MapDto map in dto.definitions)
        {
            if (!MapDefinitionId.TryCreate(map.id, out MapDefinitionId id) || maps.ContainsKey(id))
            {
                error = $"'{MapsFile}' has an invalid or repeated map ID.";
                return null;
            }

            NavigationGrid? grid = BuildGrid(map.navigation, out string gridError);
            if (grid == null)
            {
                error = $"Map '{map.id}': {gridError}";
                return null;
            }

            maps.Add(id, new ClientMap(id, map.displayName ?? string.Empty, map.scene ?? string.Empty, grid));
        }

        return maps;
    }

    private static Dictionary<JobDefinitionId, ClientJob>? ParseJobs(byte[] jobsBytes, out string error)
    {
        error = string.Empty;
        JobsDto? dto = FromJson<JobsDto>(jobsBytes);
        if (dto == null || dto.definitions == null || dto.schemaVersion != SupportedSchemaVersion)
        {
            error = $"'{JobsFile}' is not readable or has an unsupported schema version.";
            return null;
        }

        var jobs = new Dictionary<JobDefinitionId, ClientJob>();
        foreach (JobDto job in dto.definitions)
        {
            if (!JobDefinitionId.TryCreate(job.id, out JobDefinitionId id) || jobs.ContainsKey(id))
            {
                error = $"'{JobsFile}' has an invalid or repeated job ID.";
                return null;
            }

            if (!IsLogicalKey(job.prefab))
            {
                error = $"Job '{job.id}': prefab is not a logical key.";
                return null;
            }

            jobs.Add(id, new ClientJob(id, job.displayName ?? string.Empty, job.prefab));
        }

        return jobs;
    }

    private static Dictionary<MonsterDefinitionId, ClientMonster>? ParseMonsters(
        byte[] monstersBytes,
        out string error)
    {
        error = string.Empty;
        MonstersDto? dto = FromJson<MonstersDto>(monstersBytes);
        if (dto == null || dto.definitions == null || dto.schemaVersion != SupportedSchemaVersion)
        {
            error = $"'{MonstersFile}' is not readable or has an unsupported schema version.";
            return null;
        }

        var monsters = new Dictionary<MonsterDefinitionId, ClientMonster>();
        foreach (MonsterDto monster in dto.definitions)
        {
            if (!MonsterDefinitionId.TryCreate(monster.id, out MonsterDefinitionId id) || monsters.ContainsKey(id))
            {
                error = $"'{MonstersFile}' has an invalid or repeated monster ID.";
                return null;
            }

            if (!IsLogicalKey(monster.prefab))
            {
                error = $"Monster '{monster.id}': prefab is not a logical key.";
                return null;
            }

            if (!IsLogicalKey(monster.icon))
            {
                error = $"Monster '{monster.id}': icon is not a logical key.";
                return null;
            }

            monsters.Add(
                id,
                new ClientMonster(id, monster.displayName ?? string.Empty, monster.prefab, monster.icon));
        }

        return monsters;
    }

    private static Dictionary<ItemDefinitionId, ClientItem>? ParseItems(byte[] itemsBytes, out string error)
    {
        error = string.Empty;
        ItemsDto? dto = FromJson<ItemsDto>(itemsBytes);
        if (dto == null || dto.definitions == null || dto.schemaVersion != SupportedSchemaVersion)
        {
            error = $"'{ItemsFile}' is not readable or has an unsupported schema version.";
            return null;
        }

        var items = new Dictionary<ItemDefinitionId, ClientItem>();
        foreach (ItemDto item in dto.definitions)
        {
            if (!ItemDefinitionId.TryCreate(item.id, out ItemDefinitionId id) || items.ContainsKey(id))
            {
                error = $"'{ItemsFile}' has an invalid or repeated item ID.";
                return null;
            }

            if (!IsLogicalKey(item.model))
            {
                error = $"Item '{item.id}': model is not a logical key.";
                return null;
            }

            if (!IsLogicalKey(item.icon))
            {
                error = $"Item '{item.id}': icon is not a logical key.";
                return null;
            }

            items.Add(id, new ClientItem(id, item.displayName ?? string.Empty, item.model, item.icon));
        }

        return items;
    }

    private static Dictionary<SkillDefinitionId, ClientSkill>? ParseSkills(byte[] skillsBytes, out string error)
    {
        error = string.Empty;
        SkillsDto? dto = FromJson<SkillsDto>(skillsBytes);
        if (dto == null || dto.definitions == null || dto.schemaVersion != SupportedSchemaVersion)
        {
            error = $"'{SkillsFile}' is not readable or has an unsupported schema version.";
            return null;
        }

        var skills = new Dictionary<SkillDefinitionId, ClientSkill>();
        foreach (SkillDto skill in dto.definitions)
        {
            if (!SkillDefinitionId.TryCreate(skill.id, out SkillDefinitionId id) || skills.ContainsKey(id))
            {
                error = $"'{SkillsFile}' has an invalid or repeated skill ID.";
                return null;
            }

            if (!TryParseTargetType(skill.targetType, out SkillTargetType targetType))
            {
                error = $"Skill '{skill.id}': targetType is not enemy or self.";
                return null;
            }

            if (!IsLogicalKey(skill.icon))
            {
                error = $"Skill '{skill.id}': icon is not a logical key.";
                return null;
            }

            skills.Add(id, new ClientSkill(id, skill.displayName ?? string.Empty, targetType, skill.icon));
        }

        return skills;
    }

    private static Dictionary<StatusDefinitionId, ClientStatusEffect>? ParseStatusEffects(
        byte[] statusBytes,
        out string error)
    {
        error = string.Empty;
        StatusEffectsDto? dto = FromJson<StatusEffectsDto>(statusBytes);
        if (dto == null || dto.definitions == null || dto.schemaVersion != SupportedSchemaVersion)
        {
            error = $"'{StatusEffectsFile}' is not readable or has an unsupported schema version.";
            return null;
        }

        var effects = new Dictionary<StatusDefinitionId, ClientStatusEffect>();
        foreach (StatusEffectDto effect in dto.definitions)
        {
            if (!StatusDefinitionId.TryCreate(effect.id, out StatusDefinitionId id) || effects.ContainsKey(id))
            {
                error = $"'{StatusEffectsFile}' has an invalid or repeated status effect ID.";
                return null;
            }

            // The icon is optional; JsonUtility reads an absent one as empty.
            string? icon = string.IsNullOrEmpty(effect.icon) ? null : effect.icon;
            if (icon != null && !IsLogicalKey(icon))
            {
                error = $"Status effect '{effect.id}': icon is not a logical key.";
                return null;
            }

            effects.Add(id, new ClientStatusEffect(id, effect.displayName ?? string.Empty, icon));
        }

        return effects;
    }

    private static bool TryParseTargetType(string? text, out SkillTargetType targetType)
    {
        switch (text)
        {
            case "enemy":
                targetType = SkillTargetType.Enemy;
                return true;
            case "self":
                targetType = SkillTargetType.Self;
                return true;
            default:
                targetType = SkillTargetType.Enemy;
                return false;
        }
    }

    // The content tools enforce the same grammar. It is checked again here because a key that is a path would still
    // resolve as an Addressables address, which the logical-key rule exists to prevent.
    private static bool IsLogicalKey([NotNullWhen(true)] string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        foreach (char character in key)
        {
            bool isAllowed = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '_'
                || character == '-';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }

    private static NavigationGrid? BuildGrid(NavigationDto? navigation, out string error)
    {
        error = string.Empty;
        if (navigation == null || navigation.legend == null || navigation.cellRows == null)
        {
            error = "navigation is missing.";
            return null;
        }

        int columns = navigation.columns;
        int rows = navigation.rows;
        bool hasUsableSize = columns >= 1 && columns <= NavigationGrid.MaxCellsPerAxis
            && rows >= 1 && rows <= NavigationGrid.MaxCellsPerAxis;
        if (!hasUsableSize || navigation.cellRows.Length != rows)
        {
            error = "navigation rows do not match its size.";
            return null;
        }

        var legend = new Dictionary<char, NavigationCell>();
        foreach (LegendDto entry in navigation.legend)
        {
            if (entry.symbol == null || entry.symbol.Length != 1 || legend.ContainsKey(entry.symbol[0])
                || !TryParseSurface(entry.surface, out NavigationSurface surface)
                || !TryParseAxis(entry.axis, out RampAxis axis))
            {
                error = "navigation legend is malformed.";
                return null;
            }

            try
            {
                legend.Add(entry.symbol[0], new NavigationCell(surface, axis, entry.heightAtMin, entry.heightAtMax));
            }
            catch (ArgumentException exception)
            {
                error = $"navigation legend '{entry.symbol}': {exception.Message}";
                return null;
            }
        }

        var cells = new NavigationCell[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            string text = navigation.cellRows[row];
            if (text == null || text.Length != columns)
            {
                error = $"navigation row {row} has the wrong length.";
                return null;
            }

            for (int column = 0; column < columns; column++)
            {
                if (!legend.TryGetValue(text[column], out NavigationCell cell))
                {
                    error = $"navigation row {row} uses a symbol outside the legend.";
                    return null;
                }

                cells[row * columns + column] = cell;
            }
        }

        try
        {
            return new NavigationGrid(
                columns,
                rows,
                navigation.cellSize,
                navigation.originX,
                navigation.originZ,
                navigation.agentRadius,
                navigation.maxStepHeight,
                cells);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static bool TryParseSurface(string? text, out NavigationSurface surface)
    {
        switch (text)
        {
            case "floor":
                surface = NavigationSurface.Floor;
                return true;
            case "wall":
                surface = NavigationSurface.Wall;
                return true;
            case "obstacle":
                surface = NavigationSurface.Obstacle;
                return true;
            case "npcMarker":
                surface = NavigationSurface.NpcMarker;
                return true;
            case "gate":
                surface = NavigationSurface.Gate;
                return true;
            default:
                surface = NavigationSurface.Wall;
                return false;
        }
    }

    private static bool TryParseAxis(string? text, out RampAxis axis)
    {
        switch (text)
        {
            case "none":
                axis = RampAxis.None;
                return true;
            case "x":
                axis = RampAxis.X;
                return true;
            case "z":
                axis = RampAxis.Z;
                return true;
            default:
                axis = RampAxis.None;
                return false;
        }
    }

    private static T? FromJson<T>(byte[] bytes)
        where T : class
    {
        try
        {
            return JsonUtility.FromJson<T>(new UTF8Encoding(false, true).GetString(bytes));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string ComputeHash(byte[] content)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(content);
            var text = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                text.Append(value.ToString("x2"));
            }

            return text.ToString();
        }
    }

    // JsonUtility binds by field name, so these fields carry the package's JSON spelling instead of the usual
    // naming. Each has an initializer because nothing but JsonUtility ever assigns them.
    // ReSharper disable InconsistentNaming, RedundantDefaultMemberInitializer
    [Serializable]
    private sealed class ManifestDto
    {
        public int schemaVersion = 0;
        public string? clientContentVersion = string.Empty;
        public ManifestFileDto[] files = Array.Empty<ManifestFileDto>();
    }

    [Serializable]
    private sealed class ManifestFileDto
    {
        public string path = string.Empty;
        public string sha256 = string.Empty;
    }

    [Serializable]
    private sealed class MapsDto
    {
        public int schemaVersion = 0;
        public MapDto[] definitions = Array.Empty<MapDto>();
    }

    [Serializable]
    private sealed class MapDto
    {
        public string? id = string.Empty;
        public string? displayName = string.Empty;
        public string? scene = string.Empty;
        public NavigationDto? navigation = null;
    }

    [Serializable]
    private sealed class NavigationDto
    {
        public float cellSize = 0f;
        public float originX = 0f;
        public float originZ = 0f;
        public float agentRadius = 0f;
        public float maxStepHeight = 0f;
        public int columns = 0;
        public int rows = 0;
        public LegendDto[] legend = Array.Empty<LegendDto>();
        public string[] cellRows = Array.Empty<string>();
    }

    [Serializable]
    private sealed class LegendDto
    {
        public string? symbol = string.Empty;
        public string? surface = string.Empty;
        public string? axis = string.Empty;
        public float heightAtMin = 0f;
        public float heightAtMax = 0f;
    }

    [Serializable]
    private sealed class JobsDto
    {
        public int schemaVersion = 0;
        public JobDto[] definitions = Array.Empty<JobDto>();
    }

    [Serializable]
    private sealed class JobDto
    {
        public string? id = string.Empty;
        public string? displayName = string.Empty;
        public string? prefab = string.Empty;
    }

    [Serializable]
    private sealed class MonstersDto
    {
        public int schemaVersion = 0;
        public MonsterDto[] definitions = Array.Empty<MonsterDto>();
    }

    [Serializable]
    private sealed class MonsterDto
    {
        public string? id = string.Empty;
        public string? displayName = string.Empty;
        public string? prefab = string.Empty;
        public string? icon = string.Empty;
    }

    [Serializable]
    private sealed class ItemsDto
    {
        public int schemaVersion = 0;
        public ItemDto[] definitions = Array.Empty<ItemDto>();
    }

    [Serializable]
    private sealed class ItemDto
    {
        public string? id = string.Empty;
        public string? displayName = string.Empty;
        public string? model = string.Empty;
        public string? icon = string.Empty;
    }

    [Serializable]
    private sealed class SkillsDto
    {
        public int schemaVersion = 0;
        public SkillDto[] definitions = Array.Empty<SkillDto>();
    }

    [Serializable]
    private sealed class StatusEffectsDto
    {
        public int schemaVersion = 0;
        public StatusEffectDto[] definitions = Array.Empty<StatusEffectDto>();
    }

    [Serializable]
    private sealed class StatusEffectDto
    {
        public string? id = string.Empty;
        public string? displayName = string.Empty;
        public string? icon = string.Empty;
    }

    [Serializable]
    private sealed class SkillDto
    {
        public string? id = string.Empty;
        public string? displayName = string.Empty;
        public string? targetType = string.Empty;
        public string? icon = string.Empty;
    }
    // ReSharper restore InconsistentNaming, RedundantDefaultMemberInitializer
}
}
