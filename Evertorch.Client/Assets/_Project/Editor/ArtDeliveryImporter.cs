using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Editor
{
/// <summary>
///     Brings an accepted art delivery into the project (Content Pipeline §6; Prototype Content §2.1). It checks every
///     file it copies against the delivery's <c>manifest.sha256</c>, copies only the models, clips, and textures the
///     manifest names, makes their URP Lit materials, writes each rig's <see cref="BodyClips" />, and builds the
///     prefabs of each body, weapon, and worn piece in <see cref="ArtPaths.StagedPrefabsRoot" />. Every player outfit it
///     builds takes the body sockets of every kept manifest. It only reads the delivery's folder, and running it again
///     over the same delivery changes nothing.
/// </summary>
public static class ArtDeliveryImporter
{
    /// <summary>
    ///     The outfit whose model defines the humanoid avatar that every outfit and clip copies, since the skeleton is
    ///     shared (the art brief, §5).
    /// </summary>
    public const string AvatarSource = "character_adventurer";

    public const string OverheadAnchor = "Anchor_Overhead";
    public const string ProjectileAnchor = "Anchor_Projectile";
    public const string PickAnchor = "Anchor_Pick";
    public const string TrimSlot = "Trim";
    public const string WeaponSocket = "RightHand_Weapon";

    // A ground pickup's anchors, derived from its body laid flat: the overhead stands this far above it.
    private const float PickupOverheadClearance = 0.25f;

    // How far apart a worn piece's parts lie on the ground, such as a pair of boots.
    private const float PickupPartGap = 0.04f;

    private const string LitShader = "Universal Render Pipeline/Lit";
    private const string CommandLineOption = "-artDelivery";

    [MenuItem("Evertorch/Art/Import Delivery...")]
    private static void ImportFromMenu()
    {
        string folder = EditorUtility.OpenFolderPanel("Import an art delivery", string.Empty, string.Empty);
        if (folder.Length > 0)
        {
            Import(folder);
        }
    }

    /// <summary>
    ///     The batch method, run with the editor closed and the delivery's folder after <c>-artDelivery</c>; it exits 0
    ///     when the delivery was imported.
    /// </summary>
    public static void ImportFromCommandLine()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, CommandLineOption);
        try
        {
            if (index < 0 || index + 1 >= arguments.Length)
            {
                throw new ArgumentException($"Name the delivery's folder with {CommandLineOption} <folder>.");
            }

            Import(arguments[index + 1]);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    ///     Puts every staged body in its place under its key, in the default Addressables group, where the graybox body
    ///     it replaces was (Content Pipeline §6); the graybox prefab and its entry go.
    /// </summary>
    [MenuItem("Evertorch/Art/Address Staged Bodies")]
    public static void AddressStaged()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (!Directory.Exists(ArtPaths.StagedPrefabsRoot))
        {
            return;
        }

        foreach (string staged in Directory.GetFiles(ArtPaths.StagedPrefabsRoot, "*.prefab").OrderBy(path => path))
        {
            string path = staged.Replace('\\', '/');
            string key = Path.GetFileNameWithoutExtension(path);
            string target = ArtPaths.AddressedPrefabPath(key);
            if (File.Exists(target))
            {
                settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(target));
                AssetDatabase.DeleteAsset(target);
            }

            string error = AssetDatabase.MoveAsset(path, target);
            if (error.Length > 0)
            {
                throw new InvalidOperationException($"{path} could not move to {target}: {error}");
            }

            AddressableAssetEntry entry =
                settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(target), settings.DefaultGroup);
            entry.address = key;
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>
    ///     A weapon's grip and the key of its ground pickup, which its manifest does not name.
    /// </summary>
    public static bool TryGetWeapon(string key, out WeaponGrip grip, out string pickupKey)
    {
        switch (key)
        {
            case "weapon_training_sword":
                grip = WeaponGrip.Sword;
                pickupKey = "pickup_training_sword";
                return true;
            case "weapon_training_staff":
                grip = WeaponGrip.Staff;
                pickupKey = "pickup_training_staff";
                return true;
            default:
                grip = WeaponGrip.Sword;
                pickupKey = string.Empty;
                return false;
        }
    }

    public static void Import(string deliveryFolder)
    {
        string folder = deliveryFolder.Replace('\\', '/').TrimEnd('/');
        string manifestText = File.ReadAllText($"{folder}/{ArtPaths.ManifestFile}");
        var manifest = ArtManifest.Parse(manifestText);
        IReadOnlyDictionary<string, string> hashes = ReadHashes(File.ReadAllText($"{folder}/{ArtPaths.HashesFile}"));
        IReadOnlyList<ArtCopy> copies = ArtPaths.Map(manifest);
        foreach (ArtCopy copy in copies)
        {
            RequireHash(folder, copy.Source, hashes);
        }

        RequireHash(folder, ArtPaths.ManifestFile, hashes);
        foreach (ArtManifestAsset asset in manifest.Assets)
        {
            if (asset.Kind == "weapon" && !TryGetWeapon(asset.Key, out _, out _))
            {
                throw new InvalidOperationException($"The importer knows no grip for the weapon {asset.Key}.");
            }
        }

        foreach (ArtCopy copy in copies)
        {
            CopyIfChanged($"{folder}/{copy.Source}", copy.Target);
        }

        string kept = ArtPaths.DeliveryFolder(manifest.Delivery);
        CopyIfChanged($"{folder}/{ArtPaths.ManifestFile}", $"{kept}/{ArtPaths.ManifestFile}");
        CopyIfChanged($"{folder}/{ArtPaths.HashesFile}", $"{kept}/{ArtPaths.HashesFile}");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        foreach (ArtManifestAsset asset in manifest.Assets)
        {
            foreach (string texture in asset.Textures)
            {
                string path = ArtPaths.TexturePath(asset, texture);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                ArtImportSettings.ApplyTexture(importer, path);
                Reimport(importer);
            }
        }

        var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (ArtManifestAsset asset in manifest.Assets)
        {
            foreach (string slot in asset.Materials)
            {
                materials[MaterialKey(asset.Key, slot)] = MakeMaterial(asset, slot);
            }
        }

        foreach (ArtManifestAsset asset in manifest.Assets.OrderBy(asset => asset.Key == AvatarSource ? 0 : 1))
        {
            ConfigureModel(asset, materials);
        }

        foreach (ArtManifestClip clip in manifest.Clips)
        {
            ConfigureClip(clip);
        }

        foreach (IGrouping<string, ArtManifestClip> rig in manifest.Clips.GroupBy(clip => clip.Rig))
        {
            WriteClips(rig.Key, rig.ToList());
        }

        foreach (ArtManifestAsset asset in manifest.Assets)
        {
            BuildPrefabs(asset, materials);
        }

        AddSocketsToBuiltOutfits(manifest);
        AssetDatabase.SaveAssets();
        Debug.Log(
            $"ArtDeliveryImported: {manifest.Delivery}, {manifest.Assets.Count} assets, {manifest.Clips.Count} clips");
    }

    /// <summary>
    ///     <c>manifest.sha256</c>'s lines, "&lt;hash&gt;  &lt;path&gt;", by path.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadHashes(string text)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            int gap = trimmed.IndexOf("  ", StringComparison.Ordinal);
            if (gap == 64)
            {
                hashes[trimmed.Substring(66)] = trimmed.Substring(0, 64).ToLowerInvariant();
            }
        }

        return hashes;
    }

    public static string HashOf(string path)
    {
        using var sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
    }

    private static void RequireHash(string folder, string source, IReadOnlyDictionary<string, string> hashes)
    {
        if (!hashes.TryGetValue(source, out string? expected))
        {
            throw new InvalidOperationException($"manifest.sha256 does not list {source}.");
        }

        string actual = HashOf($"{folder}/{source}");
        if (actual != expected)
        {
            throw new InvalidOperationException($"{source} does not match its hash in manifest.sha256.");
        }
    }

    private static void CopyIfChanged(string source, string target)
    {
        string? directory = Path.GetDirectoryName(target);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(target) && HashOf(target) == HashOf(source))
        {
            return;
        }

        File.Copy(source, target, true);
    }

    // Every call reimports: the importer cannot tell whether a setting changed. Its .meta file is written as it was when
    // nothing did, so a second run changes no file.
    private static void Reimport(AssetImporter importer)
    {
        importer.SaveAndReimport();
    }

    private static string MaterialKey(string key, string slot)
    {
        return $"{key}/{slot}";
    }

    private static Material MakeMaterial(ArtManifestAsset asset, string slot)
    {
        string path = ArtPaths.MaterialPath(asset.Key, slot);
        Material? material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(LitShader)) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseMap", Texture(asset, "_BaseMap"));
        Texture2D? mask = Texture(asset, "_MaskMap");
        material.SetTexture("_MetallicGlossMap", mask);
        material.SetTexture("_OcclusionMap", mask);
        // With its mask, the map's channels decide; without one, a plain painted surface rather than a mirror.
        material.SetFloat("_Metallic", mask != null ? 1f : 0f);
        material.SetFloat("_Smoothness", mask != null ? 1f : 0.5f);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        SetKeyword(material, "_METALLICSPECGLOSSMAP", mask != null);
        SetKeyword(material, "_OCCLUSIONMAP", mask != null);
        Texture2D? normal = Texture(asset, "_Normal");
        material.SetTexture("_BumpMap", normal);
        SetKeyword(material, "_NORMALMAP", normal != null);
        Texture2D? emission = Texture(asset, "_Emission");
        material.SetTexture("_EmissionMap", emission);
        material.SetColor("_EmissionColor", emission != null ? Color.white : Color.black);
        SetKeyword(material, "_EMISSION", emission != null);
        material.globalIlluminationFlags = emission != null
            ? MaterialGlobalIlluminationFlags.RealtimeEmissive
            : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D? Texture(ArtManifestAsset asset, string suffix)
    {
        string? texture = asset.Textures.FirstOrDefault(name => name.EndsWith(suffix + ".png"));
        return texture == null
            ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPaths.TexturePath(asset, texture));
    }

    private static void SetKeyword(Material material, string keyword, bool isOn)
    {
        if (isOn)
        {
            material.EnableKeyword(keyword);
        }
        else
        {
            material.DisableKeyword(keyword);
        }
    }

    private static void ConfigureModel(ArtManifestAsset asset, IReadOnlyDictionary<string, Material> materials)
    {
        string path = ArtPaths.ModelPath(asset);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        ArtImportSettings.ApplyModel(importer, false);
        foreach (string slot in asset.Materials)
        {
            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(typeof(Material), slot),
                materials[MaterialKey(asset.Key, slot)]);
        }

        switch (asset.Kind)
        {
            case "humanoid":
                importer.animationType = ModelImporterAnimationType.Human;
                if (asset.Key == AvatarSource)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.humanDescription = DescribeHuman(importer, path);
                }
                else
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    importer.sourceAvatar = HumanoidAvatar();
                }

                break;
            case "monster":
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                break;
            default:
                importer.animationType = ModelImporterAnimationType.None;
                break;
        }

        Reimport(importer);
    }

    // The bones by name, with no stretch, half twist, no feet spacing, and translation kept, as the delivery's own
    // validation set them; Unity's defaults would move the socket out of the hand and lift the dead off the ground.
    private static HumanDescription DescribeHuman(ModelImporter importer, string path)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
        HumanDescription description = importer.humanDescription;
        description.human = HumanTrait.BoneName
            .Where(human => transforms.Any(bone => bone.name == human.Replace(" ", string.Empty)))
            .Select(human => new HumanBone
            {
                humanName = human,
                boneName = human.Replace(" ", string.Empty),
                limit = new HumanLimit { useDefaultValues = true }
            })
            .ToArray();
        description.skeleton = transforms
            .Select(bone => new SkeletonBone
            {
                name = bone.name,
                position = bone.localPosition,
                rotation = bone.localRotation,
                scale = bone.localScale
            })
            .ToArray();
        description.armStretch = 0f;
        description.legStretch = 0f;
        description.upperArmTwist = 0.5f;
        description.lowerArmTwist = 0.5f;
        description.upperLegTwist = 0.5f;
        description.lowerLegTwist = 0.5f;
        description.feetSpacing = 0f;
        description.hasTranslationDoF = true;
        return description;
    }

    private static Avatar HumanoidAvatar()
    {
        string path = $"{ArtPaths.ModelsRoot}/{AvatarSource}/{AvatarSource}.fbx";
        Avatar? avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        return avatar != null
            ? avatar
            : throw new InvalidOperationException($"The humanoid avatar is missing: import {AvatarSource} first.");
    }

    private static void ConfigureClip(ArtManifestClip clip)
    {
        string path = ArtPaths.ClipPath(clip);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        ArtImportSettings.ApplyModel(importer, true);
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        if (clip.Rig == ArtPaths.HumanoidRig)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = HumanoidAvatar();
        }
        else
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        Reimport(importer);
        ModelImporterClipAnimation[] takes = importer.clipAnimations.Length > 0
            ? importer.clipAnimations
            : importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation take in takes)
        {
            take.name = clip.Name;
            take.events = Array.Empty<AnimationEvent>();
            take.loopTime = clip.Loop;
            take.loopPose = false;
            take.lockRootRotation = true;
            take.keepOriginalOrientation = true;
            take.lockRootPositionXZ = true;
            take.keepOriginalPositionXZ = true;
            take.lockRootHeightY = true;
            take.keepOriginalPositionY = true;
            take.heightFromFeet = false;
        }

        importer.clipAnimations = takes;
        Reimport(importer);
    }

    public static AnimationClip? LoadClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview", StringComparison.Ordinal));
    }

    private static void WriteClips(string rig, IReadOnlyList<ArtManifestClip> clips)
    {
        string path = ArtPaths.ClipsAssetPath(rig);
        BodyClips? asset = AssetDatabase.LoadAssetAtPath<BodyClips>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<BodyClips>();
            AssetDatabase.CreateAsset(asset, path);
        }

        asset.Configure(
            rig,
            clips.Select(clip => new BodyClips.Entry(
                    clip.Name,
                    LoadClip(ArtPaths.ClipPath(clip))
                    ?? throw new InvalidOperationException($"{ArtPaths.ClipPath(clip)} holds no clip."),
                    clip.Frames,
                    clip.Loop,
                    clip.Markers.OrderBy(marker => marker.Value)
                        .Select(marker => new BodyClips.Marker(marker.Key, marker.Value))
                        .ToArray(),
                    clip.CalibrationSpeed))
                .ToArray());
        EditorUtility.SetDirty(asset);
    }

    private static void BuildPrefabs(ArtManifestAsset asset, IReadOnlyDictionary<string, Material> materials)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPaths.ModelPath(asset));
        if (asset.Kind == "weapon")
        {
            TryGetWeapon(asset.Key, out WeaponGrip grip, out string pickupKey);
            BuildHeldWeapon(asset.Key, model, grip);
            BuildPickup(pickupKey, model);
            return;
        }

        if (asset.Kind == ArtManifestAsset.WornKind)
        {
            BuildWorn(asset, model, materials);
            BuildWornPickup(asset, model);
            return;
        }

        var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            body.name = asset.Key;
            ConfigureBody(body, asset, materials);
            PrefabUtility.SaveAsPrefabAsset(body, PrefabTarget(asset.Key));
        }
        finally
        {
            Object.DestroyImmediate(body);
        }
    }

    private static void ConfigureBody(GameObject body, ArtManifestAsset asset,
        IReadOnlyDictionary<string, Material> materials)
    {
        foreach (SkinnedMeshRenderer skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            skin.quality = SkinQuality.Bone2;
        }

        var trimSlots = new List<EntityBody.TrimSlot>();
        if (materials.TryGetValue(MaterialKey(asset.Key, TrimSlot), out Material? trim))
        {
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = renderer.sharedMaterials;
                for (int index = 0; index < shared.Length; index++)
                {
                    if (shared[index] == trim)
                    {
                        trimSlots.Add(new EntityBody.TrimSlot(renderer, index));
                    }
                }
            }
        }

        if (!body.TryGetComponent(out EntityBody entityBody))
        {
            entityBody = body.AddComponent<EntityBody>();
        }

        entityBody.Configure(
            Find(body, OverheadAnchor),
            Find(body, ProjectileAnchor),
            Find(body, PickAnchor),
            asset.PickRadius,
            trimSlots.ToArray());
        if (asset.Kind == "humanoid")
        {
            AddSockets(body, asset.Key, KeptSockets());
        }

        body.TryGetComponent(out Animator? animator);
        if (animator == null && asset.Kind == "humanoid")
        {
            // A model that copies another's avatar and carries no clips of its own is imported without an Animator.
            animator = body.AddComponent<Animator>();
            animator.avatar = HumanoidAvatar();
        }

        string rig = asset.Kind == "humanoid" ? ArtPaths.HumanoidRig : asset.Key;
        BodyClips? clips = AssetDatabase.LoadAssetAtPath<BodyClips>(ArtPaths.ClipsAssetPath(rig));
        if (animator == null || clips == null)
        {
            return;
        }

        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (!body.TryGetComponent(out BodyAnimator bodyAnimator))
        {
            bodyAnimator = body.AddComponent<BodyAnimator>();
        }

        bodyAnimator.Configure(clips);
    }

    // Every kept manifest's socket tables, so rebuilding an outfit from an earlier delivery keeps the sockets a later
    // one measured (Content Pipeline §6).
    private static IReadOnlyList<ArtManifestSocket> KeptSockets()
    {
        if (!Directory.Exists(ArtPaths.DeliveriesRoot))
        {
            return Array.Empty<ArtManifestSocket>();
        }

        return Directory.GetDirectories(ArtPaths.DeliveriesRoot)
            .Select(folder => $"{folder.Replace('\\', '/')}/{ArtPaths.ManifestFile}")
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => ArtManifest.Parse(File.ReadAllText(path)).Sockets)
            .ToList();
    }

    // A socket is an empty transform under its bone, never part of the model file or the avatar.
    private static void AddSockets(GameObject body, string outfit, IEnumerable<ArtManifestSocket> sockets)
    {
        foreach (ArtManifestSocket socket in sockets.Where(socket => socket.Outfit == outfit))
        {
            Transform bone = Find(body, socket.Bone) ?? throw new InvalidOperationException(
                $"{outfit} has no bone {socket.Bone} for {socket.Name}.");
            Transform? placed = Find(body, socket.Name);
            if (placed != null && placed.parent != bone)
            {
                Object.DestroyImmediate(placed.gameObject);
                placed = null;
            }

            placed ??= NewAnchor(bone.gameObject, socket.Name, Vector3.zero);
            placed.localPosition = socket.Position;
            placed.localRotation = Quaternion.Normalize(socket.Rotation);
            placed.localScale = socket.Scale;
        }
    }

    // An outfit this delivery measured but did not deliver keeps its prefab and gains the sockets in place.
    private static void AddSocketsToBuiltOutfits(ArtManifest manifest)
    {
        IReadOnlyList<ArtManifestSocket> sockets = KeptSockets();
        foreach (string outfit in manifest.Sockets.Select(socket => socket.Outfit).Distinct())
        {
            if (manifest.Assets.Any(asset => asset.Key == outfit))
            {
                continue;
            }

            string path = BuiltPrefab(outfit) ?? throw new InvalidOperationException(
                $"The sockets' outfit {outfit} has no prefab: import its delivery first.");
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                AddSockets(contents, outfit, sockets);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }

    private static string? BuiltPrefab(string key)
    {
        string target = PrefabTarget(key);
        return File.Exists(target) ? target : null;
    }

    private static void BuildHeldWeapon(string key, GameObject model, WeaponGrip grip)
    {
        var weapon = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            weapon.name = key;
            HeldWeapon held = weapon.AddComponent<HeldWeapon>();
            held.Configure(grip);
            PrefabUtility.SaveAsPrefabAsset(weapon, PrefabTarget(key));
        }
        finally
        {
            Object.DestroyImmediate(weapon);
        }
    }

    // The weapon laid flat on the ground: its grip-to-tip axis along the ground, its thin side down, centred on the
    // drop's position; the anchors follow the laid body rather than the manifest, which measured it upright.
    private static void BuildPickup(string pickupKey, GameObject model)
    {
        var pickup = new GameObject(pickupKey);
        try
        {
            var laid = (GameObject)PrefabUtility.InstantiatePrefab(model, pickup.transform);
            laid.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Bounds bounds = BoundsOf(laid);
            laid.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            foreach (Transform anchor in laid.GetComponentsInChildren<Transform>(true)
                         .Where(child => child.name.StartsWith("Anchor_", StringComparison.Ordinal))
                         .ToList())
            {
                Object.DestroyImmediate(anchor.gameObject);
            }

            bounds = BoundsOf(laid);
            Transform overhead =
                NewAnchor(pickup, OverheadAnchor, new Vector3(0f, bounds.max.y + PickupOverheadClearance, 0f));

            // No pick point: a drop keeps the sphere every drop has, so a click on a monster standing over it still
            // takes the monster (Prototype Content §4).
            EntityBody body = pickup.AddComponent<EntityBody>();
            body.Configure(overhead, null, null, EntityBody.MaxPickRadius, Array.Empty<EntityBody.TrimSlot>());
            PrefabUtility.SaveAsPrefabAsset(pickup, PrefabTarget(pickupKey));
        }
        finally
        {
            Object.DestroyImmediate(pickup);
        }
    }

    // A worn piece's parts, each its own node of the model, hang on the body's sockets when worn; the prefab names
    // which, and the material slot of each that takes the player's tint.
    private static void BuildWorn(ArtManifestAsset asset, GameObject model,
        IReadOnlyDictionary<string, Material> materials)
    {
        var worn = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            worn.name = asset.Key;
            materials.TryGetValue(MaterialKey(asset.Key, TrimSlot), out Material? trim);
            var parts = new List<WornPiece.Part>();
            foreach (ArtManifestPart part in asset.Parts)
            {
                Transform node = Find(worn, part.Node) ?? throw new InvalidOperationException(
                    $"{asset.Key} has no node {part.Node}.");
                Material[] shared = node.TryGetComponent(out Renderer renderer)
                    ? renderer.sharedMaterials
                    : Array.Empty<Material>();
                parts.Add(new WornPiece.Part(node, part.Socket, trim == null ? -1 : Array.IndexOf(shared, trim)));
            }

            worn.AddComponent<WornPiece>().Configure(parts.ToArray());
            PrefabUtility.SaveAsPrefabAsset(worn, PrefabTarget(asset.Key));
        }
        finally
        {
            Object.DestroyImmediate(worn);
        }
    }

    // A worn piece on the ground: body armor and a shield lie face up, a garment with its outside up, and headgear,
    // footwear, and an accessory stand as worn, the parts side by side and centred on the drop's position.
    private static void BuildWornPickup(ArtManifestAsset asset, GameObject model)
    {
        var pickup = new GameObject(asset.Pickup);
        try
        {
            var laid = (GameObject)PrefabUtility.InstantiatePrefab(model, pickup.transform);
            Quaternion turn = asset.Category switch
            {
                "armor" or "shield" => Quaternion.Euler(-90f, 0f, 0f),
                "garment" => Quaternion.Euler(90f, 0f, 0f),
                _ => Quaternion.identity
            };
            float across = 0f;
            foreach (ArtManifestPart part in asset.Parts)
            {
                Transform node = Find(laid, part.Node)!;
                node.localRotation = turn;
                node.localPosition = Vector3.zero;
                Bounds bounds = BoundsOf(node.gameObject);
                node.localPosition = new Vector3(across - bounds.min.x, -bounds.min.y, -bounds.center.z);
                across += bounds.size.x + PickupPartGap;
            }

            Bounds all = BoundsOf(laid);
            laid.transform.localPosition = new Vector3(-all.center.x, 0f, 0f);
            all = BoundsOf(laid);
            Transform overhead =
                NewAnchor(pickup, OverheadAnchor, new Vector3(0f, all.max.y + PickupOverheadClearance, 0f));
            EntityBody body = pickup.AddComponent<EntityBody>();
            body.Configure(overhead, null, null, EntityBody.MaxPickRadius, Array.Empty<EntityBody.TrimSlot>());
            PrefabUtility.SaveAsPrefabAsset(pickup, PrefabTarget(asset.Pickup));
        }
        finally
        {
            Object.DestroyImmediate(pickup);
        }
    }

    // A body already addressed as art is rebuilt where it is, keeping its address; anything else waits staged until
    // it is addressed (Content Pipeline §6).
    private static string PrefabTarget(string key)
    {
        string addressed = ArtPaths.AddressedPrefabPath(key);
        GameObject? existing = AssetDatabase.LoadAssetAtPath<GameObject>(addressed);
        bool isArt = existing != null
            && (existing.TryGetComponent(out EntityBody _)
                || existing.TryGetComponent(out HeldWeapon _)
                || existing.TryGetComponent(out WornPiece _));
        if (isArt)
        {
            return addressed;
        }

        Directory.CreateDirectory(ArtPaths.StagedPrefabsRoot);
        return ArtPaths.StagedPrefabPath(key);
    }

    private static Transform NewAnchor(GameObject owner, string name, Vector3 localPosition)
    {
        Transform anchor = new GameObject(name).transform;
        anchor.SetParent(owner.transform, false);
        anchor.localPosition = localPosition;
        return anchor;
    }

    private static Bounds BoundsOf(GameObject body)
    {
        Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1))
        {
            bounds.Encapsulate(renderer.bounds);
        }

        return bounds;
    }

    public static Transform? Find(GameObject body, string name)
    {
        return body.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == name);
    }
}
}
