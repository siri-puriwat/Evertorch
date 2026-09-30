using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Evertorch.Client.Editor
{
/// <summary>
///     An art delivery's <c>manifest.json</c> (Prototype Content §2.1): its assets with their anchors and pick radius,
///     and its clips with their markers. Only the fields the importer and its checks use are read.
/// </summary>
public sealed class ArtManifest
{
    // The names that become folders and files in the project: a key or rig as the content writes its keys, a delivery
    // as its folder is named. A file is a path inside the delivery, with forward slashes and no step up.
    private static readonly Regex LogicalKey = new("^[a-z0-9_]+$");
    private static readonly Regex DeliveryName = new("^[A-Za-z0-9_-]+$");

    private ArtManifest(string delivery, IReadOnlyList<ArtManifestAsset> assets, IReadOnlyList<ArtManifestClip> clips)
    {
        Delivery = delivery;
        Assets = assets;
        Clips = clips;
    }

    public string Delivery { get; }

    public IReadOnlyList<ArtManifestAsset> Assets { get; }

    public IReadOnlyList<ArtManifestClip> Clips { get; }

    public static ArtManifest Parse(string json)
    {
        Dictionary<string, object?> root = AsObject(ManifestJson.Parse(json), "the manifest");
        if (Number(root, "fps", "the manifest") != BodyClips.FramesPerSecond)
        {
            throw new FormatException($"Manifest: fps must be {BodyClips.FramesPerSecond}.");
        }

        var assets = AsArray(Field(root, "assets", "the manifest"), "assets")
            .Select(value => ReadAsset(AsObject(value, "an asset")))
            .ToList();
        var clips = AsArray(Field(root, "clips", "the manifest"), "clips")
            .Select(value => ReadClip(AsObject(value, "a clip"), assets))
            .ToList();
        string delivery = Text(root, "delivery", "the manifest");
        RequireName(delivery, DeliveryName, "the delivery");
        return new ArtManifest(delivery, assets, clips);
    }

    private static ArtManifestAsset ReadAsset(Dictionary<string, object?> asset)
    {
        string key = Text(asset, "key", "an asset");
        RequireName(key, LogicalKey, "an asset's key");
        Dictionary<string, object?> anchorValues = AsObject(Field(asset, "anchors", key), $"{key}'s anchors");
        var anchors = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> anchor in anchorValues)
        {
            List<object?> xyz = AsArray(anchor.Value, $"{key}'s {anchor.Key}");
            if (xyz.Count != 3)
            {
                throw new FormatException($"Manifest: {key}'s {anchor.Key} needs three numbers.");
            }

            anchors[anchor.Key] = new Vector3(
                (float)AsNumber(xyz[0], anchor.Key),
                (float)AsNumber(xyz[1], anchor.Key),
                (float)AsNumber(xyz[2], anchor.Key));
        }

        return new ArtManifestAsset(
            key,
            Text(asset, "kind", key),
            RelativeFile(Text(asset, "file", key), key),
            (int)Number(asset, "triangles", key),
            (int)Number(asset, "bones", key),
            AsArray(Field(asset, "materials", key), $"{key}'s materials").Select(value => AsText(value, key)).ToList(),
            AsArray(Field(asset, "textures", key), $"{key}'s textures")
                .Select(value => RelativeFile(AsText(value, key), key))
                .ToList(),
            anchors,
            (float)Number(asset, "pickRadius", key));
    }

    private static ArtManifestClip ReadClip(Dictionary<string, object?> clip, IReadOnlyList<ArtManifestAsset> assets)
    {
        string name = Text(clip, "name", "a clip");
        string rig = Text(clip, "rig", name);
        RequireName(rig, LogicalKey, "a clip's rig");
        string file = RelativeFile(Text(clip, "file", $"{rig} {name}"), $"{rig} {name}");
        if (rig != ArtPaths.HumanoidRig)
        {
            rig = MonsterOf(file, assets) ?? throw new FormatException(
                $"Manifest: {rig} {name}'s file '{file}' lies in no monster's folder.");
        }

        string what = $"{rig} {name}";
        var markers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> marker in AsObject(Field(clip, "markers", what), $"{what}'s markers"))
        {
            markers[marker.Key] = (int)AsNumber(marker.Value, $"{what}'s {marker.Key}");
        }

        double calibration = clip.TryGetValue("calibrationSpeedMetresPerSecond", out object? speed)
            ? AsNumber(speed, what)
            : BriefSpeed(rig, name);
        if (clip.TryGetValue("loop", out object? loop) && loop is bool isLoop)
        {
            return new ArtManifestClip(
                rig,
                name,
                file,
                (int)Number(clip, "frames", what),
                isLoop,
                markers,
                (float)calibration);
        }

        throw new FormatException($"Manifest: {what} needs a boolean loop.");
    }

    // A monster's clips lie in its own folder; ArtDelivery-02 names their rig apart from the monster's key, so the folder
    // decides which monster's body they drive: the deepest that holds the clip, and never a model outside any folder.
    private static string? MonsterOf(string clipFile, IEnumerable<ArtManifestAsset> assets)
    {
        return assets
            .Where(asset => asset.Kind == "monster" && asset.File.Contains("/"))
            .Select(asset => (asset.Key, Folder: asset.File.Substring(0, asset.File.LastIndexOf('/') + 1)))
            .Where(monster => clipFile.StartsWith(monster.Folder, StringComparison.Ordinal))
            .OrderByDescending(monster => monster.Folder.Length)
            .Select(monster => monster.Key)
            .FirstOrDefault();
    }

    // The art brief's calibration speeds (§7), for a run or move whose manifest records none: the body's walk measured
    // against a speed of 0 would freeze the clip at its first frame.
    private static double BriefSpeed(string rig, string name)
    {
        return (rig, name) switch
        {
            (ArtPaths.HumanoidRig, "run") => 5.0,
            ("monster_training_slime", "move") => 4.0,
            ("monster_forest_crawler", "move") => 4.8,
            ("monster_spark_wisp", "move") => 4.4,
            (_, "run" or "move") => throw new FormatException(
                $"Manifest: {rig} {name} has no calibrationSpeedMetresPerSecond, and the art brief gives none."),
            _ => 0.0
        };
    }

    private static void RequireName(string name, Regex pattern, string what)
    {
        if (!pattern.IsMatch(name))
        {
            throw new FormatException($"Manifest: {what} '{name}' is not a plain name.");
        }
    }

    private static string RelativeFile(string file, string what)
    {
        string[] parts = file.Split('/');
        if (file.Contains("\\") || file.Contains(":") ||
            parts.Any(part => part.Length == 0 || part == "." || part == ".."))
        {
            throw new FormatException($"Manifest: {what}'s file '{file}' is not a path inside the delivery.");
        }

        return file;
    }

    private static object? Field(Dictionary<string, object?> owner, string name, string what)
    {
        if (!owner.TryGetValue(name, out object? value))
        {
            throw new FormatException($"Manifest: {what} has no {name}.");
        }

        return value;
    }

    private static string Text(Dictionary<string, object?> owner, string name, string what)
    {
        return AsText(Field(owner, name, what), $"{what}'s {name}");
    }

    private static double Number(Dictionary<string, object?> owner, string name, string what)
    {
        return AsNumber(Field(owner, name, what), $"{what}'s {name}");
    }

    private static string AsText(object? value, string what)
    {
        return value as string ?? throw new FormatException($"Manifest: {what} must be a string.");
    }

    private static double AsNumber(object? value, string what)
    {
        return value is double number
            ? number
            : throw new FormatException(
                string.Format(CultureInfo.InvariantCulture, "Manifest: {0} must be a number.", what));
    }

    private static Dictionary<string, object?> AsObject(object? value, string what)
    {
        return value as Dictionary<string, object?> ??
            throw new FormatException($"Manifest: {what} must be an object.");
    }

    private static List<object?> AsArray(object? value, string what)
    {
        return value as List<object?> ?? throw new FormatException($"Manifest: {what} must be an array.");
    }
}

/// <summary>
///     One delivered model: a humanoid outfit, a monster, or a weapon.
/// </summary>
public sealed class ArtManifestAsset
{
    public ArtManifestAsset(
        string key,
        string kind,
        string file,
        int triangles,
        int bones,
        IReadOnlyList<string> materials,
        IReadOnlyList<string> textures,
        IReadOnlyDictionary<string, Vector3> anchors,
        float pickRadius)
    {
        Key = key;
        Kind = kind;
        File = file;
        Triangles = triangles;
        Bones = bones;
        Materials = materials;
        Textures = textures;
        Anchors = anchors;
        PickRadius = pickRadius;
    }

    public string Key { get; }

    /// <summary>
    ///     <c>humanoid</c>, <c>monster</c>, or <c>weapon</c>.
    /// </summary>
    public string Kind { get; }

    public string File { get; }

    public int Triangles { get; }

    public int Bones { get; }

    public IReadOnlyList<string> Materials { get; }

    public IReadOnlyList<string> Textures { get; }

    public IReadOnlyDictionary<string, Vector3> Anchors { get; }

    public float PickRadius { get; }
}

/// <summary>
///     One delivered clip of a rig: <c>humanoid</c>, shared by every outfit, or a monster's key.
/// </summary>
public sealed class ArtManifestClip
{
    public ArtManifestClip(
        string rig,
        string name,
        string file,
        int frames,
        bool loop,
        IReadOnlyDictionary<string, int> markers,
        float calibrationSpeed)
    {
        Rig = rig;
        Name = name;
        File = file;
        Frames = frames;
        Loop = loop;
        Markers = markers;
        CalibrationSpeed = calibrationSpeed;
    }

    public string Rig { get; }

    public string Name { get; }

    public string File { get; }

    public int Frames { get; }

    public bool Loop { get; }

    public IReadOnlyDictionary<string, int> Markers { get; }

    public float CalibrationSpeed { get; }
}
}
