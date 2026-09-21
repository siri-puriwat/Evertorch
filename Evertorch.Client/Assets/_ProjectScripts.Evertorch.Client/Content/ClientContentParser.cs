using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
/// Builds <see cref="ClientContent"/> from the bytes of a client content package. The package is generated, but it
/// reaches the client through a folder anyone can edit, so every file is checked against the manifest before use.
/// </summary>
public static class ClientContentParser
{
    public const string ManifestFile = "manifest.json";
    public const string MapsFile = "maps.json";

    private const int SupportedSchemaVersion = 1;

    /// <summary>
    /// The files a manifest lists, or an empty list with <paramref name="error"/> set.
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

        StringBuilder listing = new StringBuilder();
        foreach (ManifestFileDto entry in manifest.files.OrderBy(file => file.path, StringComparer.Ordinal))
        {
            if (!files.TryGetValue(entry.path, out byte[]? content))
            {
                error = "Content file '" + entry.path + "' is missing.";
                return null;
            }

            string hash = ComputeHash(content);
            if (!string.Equals(hash, entry.sha256, StringComparison.Ordinal))
            {
                error = "Content file '" + entry.path + "' does not match the manifest.";
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

        if (!files.TryGetValue(MapsFile, out byte[]? mapsBytes) || manifest.files.All(file => file.path != MapsFile))
        {
            error = "The content package has no '" + MapsFile + "'.";
            return null;
        }

        Dictionary<MapDefinitionId, ClientMap>? maps = ParseMaps(mapsBytes, out error);
        return maps == null ? null : new ClientContent(version, maps);
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
            error = "The content manifest has schema version " + manifest.schemaVersion + "; expected 1.";
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
            error = "'" + MapsFile + "' is not readable or has an unsupported schema version.";
            return null;
        }

        Dictionary<MapDefinitionId, ClientMap> maps = new Dictionary<MapDefinitionId, ClientMap>();
        foreach (MapDto map in dto.definitions)
        {
            if (!MapDefinitionId.TryCreate(map.id, out MapDefinitionId id) || maps.ContainsKey(id))
            {
                error = "'" + MapsFile + "' has an invalid or repeated map ID.";
                return null;
            }

            NavigationGrid? grid = BuildGrid(map.navigation, out string gridError);
            if (grid == null)
            {
                error = "Map '" + map.id + "': " + gridError;
                return null;
            }

            maps.Add(id, new ClientMap(id, map.displayName ?? string.Empty, map.scene ?? string.Empty, grid));
        }

        return maps;
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

        Dictionary<char, NavigationCell> legend = new Dictionary<char, NavigationCell>();
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
                error = "navigation legend '" + entry.symbol + "': " + exception.Message;
                return null;
            }
        }

        NavigationCell[] cells = new NavigationCell[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            string text = navigation.cellRows[row];
            if (text == null || text.Length != columns)
            {
                error = "navigation row " + row + " has the wrong length.";
                return null;
            }

            for (int column = 0; column < columns; column++)
            {
                if (!legend.TryGetValue(text[column], out NavigationCell cell))
                {
                    error = "navigation row " + row + " uses a symbol outside the legend.";
                    return null;
                }

                cells[(row * columns) + column] = cell;
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
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(content);
            StringBuilder text = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                text.Append(value.ToString("x2"));
            }

            return text.ToString();
        }
    }

    // JsonUtility binds by field name, so these fields carry the package's JSON spelling instead of the usual
    // naming. Each has an initializer because nothing but JsonUtility ever assigns them.
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
}
}
