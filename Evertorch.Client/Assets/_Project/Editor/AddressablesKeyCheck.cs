using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Evertorch.Client.Editor
{
/// <summary>
///     Checks that every presentation key in the generated client package resolves in this Unity project: entity
///     views as Addressables prefabs, map scenes as enabled build scenes. Icons are optional until a UI shows them, and
///     projectiles, which fall back to a plain sphere.
/// </summary>
public static class AddressablesKeyCheck
{
    public static AddressablesKeyCheckResult Run()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, StreamingContentLoader.FolderName);
        ClientContent? content = ReadPackage(folder, out string error);
        if (content == null)
        {
            return AddressablesKeyCheckResult.PackageUnusable(error);
        }

        AddressableAssetSettings? settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            return AddressablesKeyCheckResult.PackageUnusable("The project has no Addressables settings.");
        }

        return Check(content, CollectAddresses(settings), CollectEnabledScenes());
    }

    public static AddressablesKeyCheckResult Check(
        ClientContent content,
        IReadOnlyDictionary<string, Type?> addresses,
        IReadOnlyCollection<string> enabledScenes)
    {
        var missing = new List<string>();
        var missingOptional = new List<string>();
        foreach (ClientJob job in content.Jobs)
        {
            RequirePrefab(addresses, job.PrefabKey, $"{job.Id.Value} prefab", missing);
        }

        foreach (ClientMonster monster in content.Monsters)
        {
            RequirePrefab(addresses, monster.PrefabKey, $"{monster.Id.Value} prefab", missing);
            NoteOptional(addresses, monster.IconKey, $"{monster.Id.Value} icon", missingOptional);
            if (monster.ProjectileKey.Length > 0)
            {
                // Without an entry the client flies a plain sphere.
                NoteOptional(addresses, monster.ProjectileKey, $"{monster.Id.Value} projectile", missingOptional);
            }
        }

        foreach (ClientItem item in content.Items)
        {
            RequirePrefab(addresses, item.ModelKey, $"{item.Id.Value} model", missing);
            NoteOptional(addresses, item.IconKey, $"{item.Id.Value} icon", missingOptional);
        }

        var scenes = new HashSet<string>(enabledScenes, StringComparer.Ordinal);
        foreach (ClientMap map in content.Maps)
        {
            if (!MapSceneResolver.TryResolve(map.SceneKey, out string sceneName) || !scenes.Contains(sceneName))
            {
                missing.Add($"{map.Id.Value} scene '{map.SceneKey}' is not an enabled scene in the build settings");
            }
        }

        return new AddressablesKeyCheckResult(string.Empty, missing, missingOptional);
    }

    private static void RequirePrefab(
        IReadOnlyDictionary<string, Type?> addresses,
        string key,
        string owner,
        List<string> missing)
    {
        if (!addresses.TryGetValue(key, out Type? type))
        {
            missing.Add($"{owner} '{key}' has no Addressables entry");
        }
        else if (type == null || !typeof(GameObject).IsAssignableFrom(type))
        {
            missing.Add($"{owner} '{key}' is not a prefab");
        }
    }

    private static void NoteOptional(
        IReadOnlyDictionary<string, Type?> addresses,
        string key,
        string owner,
        List<string> missingOptional)
    {
        if (!addresses.ContainsKey(key))
        {
            missingOptional.Add($"{owner} '{key}'");
        }
    }

    private static ClientContent? ReadPackage(string folder, out string error)
    {
        string manifestPath = Path.Combine(folder, ClientContentParser.ManifestFile);
        if (!File.Exists(manifestPath))
        {
            error = $"No client content package in StreamingAssets/{StreamingContentLoader.FolderName}. "
                + StreamingContentLoader.MissingPackageHint;
            return null;
        }

        byte[] manifest = File.ReadAllBytes(manifestPath);
        IReadOnlyList<string> names = ClientContentParser.ReadFileList(manifest, out error);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path))
            {
                files[name] = File.ReadAllBytes(path);
            }
        }

        ClientContent? content = names.Count == 0 ? null : ClientContentParser.Parse(manifest, files, out error);
        if (content == null)
        {
            error = $"{error} {StreamingContentLoader.MissingPackageHint}";
        }

        return content;
    }

    private static Dictionary<string, Type?> CollectAddresses(AddressableAssetSettings settings)
    {
        var addresses = new Dictionary<string, Type?>(StringComparer.Ordinal);
        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null)
            {
                continue;
            }

            foreach (AddressableAssetEntry entry in group.entries)
            {
                addresses[entry.address] = entry.MainAssetType;
            }
        }

        return addresses;
    }

    private static List<string> CollectEnabledScenes()
    {
        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                scenes.Add(Path.GetFileNameWithoutExtension(scene.path));
            }
        }

        return scenes;
    }
}
}
