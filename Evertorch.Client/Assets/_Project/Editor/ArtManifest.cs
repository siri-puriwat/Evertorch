using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Evertorch.Client.Editor
{
/// <summary>
///     An art delivery's <c>manifest.json</c> (Prototype Content §2.1): its assets with their anchors and pick radius,
///     and its clips with their markers. Only the fields the importer and its checks use are read.
/// </summary>
public sealed class ArtManifest
{
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
            .Select(value => ReadClip(AsObject(value, "a clip")))
            .ToList();
        return new ArtManifest(Text(root, "delivery", "the manifest"), assets, clips);
    }

    private static ArtManifestAsset ReadAsset(Dictionary<string, object?> asset)
    {
        string key = Text(asset, "key", "an asset");
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
            Text(asset, "file", key),
            (int)Number(asset, "triangles", key),
            (int)Number(asset, "bones", key),
            (int)Number(asset, "maximumWeights", key),
            AsArray(Field(asset, "materials", key), $"{key}'s materials").Select(value => AsText(value, key)).ToList(),
            AsArray(Field(asset, "textures", key), $"{key}'s textures").Select(value => AsText(value, key)).ToList(),
            anchors,
            (float)Number(asset, "pickRadius", key));
    }

    private static ArtManifestClip ReadClip(Dictionary<string, object?> clip)
    {
        string name = Text(clip, "name", "a clip");
        string rig = Text(clip, "rig", name);
        string what = $"{rig} {name}";
        var markers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> marker in AsObject(Field(clip, "markers", what), $"{what}'s markers"))
        {
            markers[marker.Key] = (int)AsNumber(marker.Value, $"{what}'s {marker.Key}");
        }

        double calibration = clip.TryGetValue("calibrationSpeedMetresPerSecond", out object? speed)
            ? AsNumber(speed, what)
            : 0.0;
        if (clip.TryGetValue("loop", out object? loop) && loop is bool isLoop)
        {
            return new ArtManifestClip(
                rig,
                name,
                Text(clip, "file", what),
                (int)Number(clip, "frames", what),
                isLoop,
                markers,
                (float)calibration);
        }

        throw new FormatException($"Manifest: {what} needs a boolean loop.");
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
        int maximumWeights,
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
        MaximumWeights = maximumWeights;
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

    public int MaximumWeights { get; }

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
