using System.Collections.Generic;

namespace Evertorch.Client.Editor
{
/// <summary>
///     Where each delivered file lives in the project (Content Pipeline §6). The importer copies by this map and the
///     contract test checks by it, so both agree on every path.
/// </summary>
public static class ArtPaths
{
    public const string ArtRoot = "Assets/_Project/Art";
    public const string ModelsRoot = ArtRoot + "/Models";
    public const string HumanoidClipsRoot = ArtRoot + "/Animations/Humanoid";
    public const string MaterialsRoot = ArtRoot + "/Materials";
    public const string DeliveriesRoot = ArtRoot + "/Deliveries";
    public const string StagedPrefabsRoot = ArtRoot + "/Prefabs";
    public const string HumanoidRig = "humanoid";
    public const string ManifestFile = "manifest.json";
    public const string HashesFile = "manifest.sha256";

    /// <summary>
    ///     Every model, texture, and clip the manifest names, from its path in the delivery to its path in the project.
    /// </summary>
    public static IReadOnlyList<ArtCopy> Map(ArtManifest manifest)
    {
        var copies = new List<ArtCopy>();
        foreach (ArtManifestAsset asset in manifest.Assets)
        {
            copies.Add(new ArtCopy(asset.File, ModelPath(asset)));
            foreach (string texture in asset.Textures)
            {
                copies.Add(new ArtCopy(texture, TexturePath(asset, texture)));
            }
        }

        foreach (ArtManifestClip clip in manifest.Clips)
        {
            copies.Add(new ArtCopy(clip.File, ClipPath(clip)));
        }

        return copies;
    }

    public static string ModelPath(ArtManifestAsset asset)
    {
        return $"{ModelsRoot}/{asset.Key}/{FileName(asset.File)}";
    }

    public static string TexturePath(ArtManifestAsset asset, string texture)
    {
        return $"{ModelsRoot}/{asset.Key}/Textures/{FileName(texture)}";
    }

    public static string ClipPath(ArtManifestClip clip)
    {
        return clip.Rig == HumanoidRig
            ? $"{HumanoidClipsRoot}/{FileName(clip.File)}"
            : $"{ModelsRoot}/{clip.Rig}/Animations/{FileName(clip.File)}";
    }

    /// <summary>
    ///     A rig's <see cref="BodyClips" />, beside its clips (Content Pipeline §6).
    /// </summary>
    public static string ClipsAssetPath(string rig)
    {
        return rig == HumanoidRig
            ? $"{HumanoidClipsRoot}/{HumanoidRig}_clips.asset"
            : $"{ModelsRoot}/{rig}/Animations/{rig}_clips.asset";
    }

    public static string MaterialPath(string key, string slot)
    {
        return $"{MaterialsRoot}/Art_{key}_{slot}.mat";
    }

    public static string StagedPrefabPath(string key)
    {
        return $"{StagedPrefabsRoot}/{key}.prefab";
    }

    public static string DeliveryFolder(string delivery)
    {
        return $"{DeliveriesRoot}/{delivery}";
    }

    public static bool IsArtAsset(string assetPath)
    {
        return assetPath.StartsWith(ModelsRoot + "/") || assetPath.StartsWith(HumanoidClipsRoot + "/");
    }

    private static string FileName(string path)
    {
        return path.Substring(path.LastIndexOf('/') + 1);
    }
}

/// <summary>
///     One delivered file: its path in the delivery, as <c>manifest.sha256</c> names it, and its path in the project.
/// </summary>
public readonly struct ArtCopy
{
    public ArtCopy(string source, string target)
    {
        Source = source;
        Target = target;
    }

    public string Source { get; }

    public string Target { get; }
}
}
