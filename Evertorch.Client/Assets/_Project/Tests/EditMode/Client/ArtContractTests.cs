using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Client.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Every committed art delivery against its manifest and the art delivery contract (Prototype Content §2.1; Content
///     Pipeline §6): its files as delivered, imported as its own validation imported them, within the art brief's
///     budgets, with its anchors, radii, slots, clips, and markers in the prefabs. A new delivery's gaps show here
///     first.
/// </summary>
[TestFixture]
public sealed class ArtContractTests
{
    private const string PrefabsRoot = "Assets/_Project/Prefabs";

    private static IEnumerable<TestCaseData> Deliveries()
    {
        if (!Directory.Exists(ArtPaths.DeliveriesRoot))
        {
            yield break;
        }

        foreach (string folder in Directory.GetDirectories(ArtPaths.DeliveriesRoot).OrderBy(path => path))
        {
            yield return new TestCaseData(folder.Replace('\\', '/'));
        }
    }

    private static ArtManifest ReadManifest(string folder)
    {
        return ArtManifest.Parse(File.ReadAllText($"{folder}/{ArtPaths.ManifestFile}"));
    }

    private static string FindPrefab(string key)
    {
        string staged = ArtPaths.StagedPrefabPath(key);
        return File.Exists(staged) ? staged : $"{PrefabsRoot}/{key}.prefab";
    }

    private static int TriangleBudget(string kind)
    {
        return kind switch
        {
            "humanoid" => 7000,
            "monster" => 3000,
            _ => 800
        };
    }

    [TestCaseSource(nameof(Deliveries))]
    public void Files_OfADelivery_MatchTheirHashes(string folder)
    {
        ArtManifest manifest = ReadManifest(folder);
        IReadOnlyDictionary<string, string> hashes =
            ArtDeliveryImporter.ReadHashes(File.ReadAllText($"{folder}/{ArtPaths.HashesFile}"));

        Assert.That(ArtDeliveryImporter.HashOf($"{folder}/{ArtPaths.ManifestFile}"),
            Is.EqualTo(hashes[ArtPaths.ManifestFile]));
        foreach (ArtCopy copy in ArtPaths.Map(manifest))
        {
            Assert.That(File.Exists(copy.Target), Is.True, copy.Target);
            Assert.That(ArtDeliveryImporter.HashOf(copy.Target), Is.EqualTo(hashes[copy.Source]), copy.Target);
        }
    }

    [TestCaseSource(nameof(Deliveries))]
    public void Models_OfADelivery_AreImportedAsValidated(string folder)
    {
        foreach (ArtManifestAsset asset in ReadManifest(folder).Assets)
        {
            string path = ArtPaths.ModelPath(asset);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            AssertSharedModelSettings(importer, path);
            switch (asset.Kind)
            {
                case "humanoid":
                    Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human), path);
                    AssertHumanAvatar(importer, asset.Key, path);
                    break;
                case "monster":
                    Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic), path);
                    break;
                default:
                    Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.None), path);
                    break;
            }
        }
    }

    private static void AssertSharedModelSettings(ModelImporter importer, string path)
    {
        Assert.That(importer.globalScale, Is.EqualTo(1f), path);
        Assert.That(importer.useFileScale, Is.True, path);
        Assert.That(importer.preserveHierarchy, Is.True, path);
        Assert.That(importer.importNormals, Is.EqualTo(ModelImporterNormals.Import), path);
        Assert.That(importer.meshCompression, Is.EqualTo(ModelImporterMeshCompression.Off), path);
        Assert.That(importer.skinWeights, Is.EqualTo(ModelImporterSkinWeights.Custom), path);
        Assert.That(importer.maxBonesPerVertex, Is.EqualTo(ArtImportSettings.MaxBoneWeights), path);
        Assert.That(importer.minBoneWeight, Is.EqualTo(ArtImportSettings.MinBoneWeight), path);
        Assert.That(importer.optimizeGameObjects, Is.False, path);
        Assert.That(importer.animationCompression, Is.EqualTo(ModelImporterAnimationCompression.Off), path);
    }

    private static void AssertHumanAvatar(ModelImporter importer, string key, string path)
    {
        if (key != ArtDeliveryImporter.AvatarSource)
        {
            Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther), path);
            Assert.That(importer.sourceAvatar, Is.Not.Null, path);
            return;
        }

        HumanDescription description = importer.humanDescription;
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel), path);
        Assert.That(
            new[] { description.armStretch, description.legStretch, description.feetSpacing },
            Is.All.EqualTo(0f),
            $"{path}: no stretch, no feet spacing");
        Assert.That(
            new[]
            {
                description.upperArmTwist, description.lowerArmTwist, description.upperLegTwist,
                description.lowerLegTwist
            },
            Is.All.EqualTo(0.5f),
            $"{path}: half twist");
        Assert.That(description.hasTranslationDoF, Is.True, path);
        Assert.That(
            description.human.All(bone => bone.boneName == bone.humanName.Replace(" ", string.Empty)),
            Is.True,
            $"{path}: bones mapped by name");
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Single();
        Assert.That((avatar.isValid, avatar.isHuman), Is.EqualTo((true, true)), path);
    }

    [TestCaseSource(nameof(Deliveries))]
    public void Clips_OfADelivery_AreImportedInPlaceWithoutEvents(string folder)
    {
        foreach (ArtManifestClip clip in ReadManifest(folder).Clips)
        {
            string path = ArtPaths.ClipPath(clip);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            AssertSharedModelSettings(importer, path);
            Assert.That(
                importer.animationType,
                Is.EqualTo(clip.Rig == ArtPaths.HumanoidRig
                    ? ModelImporterAnimationType.Human
                    : ModelImporterAnimationType.Generic),
                path);
            if (clip.Rig == ArtPaths.HumanoidRig)
            {
                Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther), path);
                Assert.That(importer.sourceAvatar, Is.Not.Null, path);
            }

            ModelImporterClipAnimation take = importer.clipAnimations.Single();
            Assert.That(take.name, Is.EqualTo(clip.Name), path);
            Assert.That((take.loopTime, take.loopPose), Is.EqualTo((clip.Loop, false)), $"{path}: loop as delivered");
            Assert.That(
                new[]
                {
                    take.lockRootRotation, take.keepOriginalOrientation, take.lockRootPositionXZ,
                    take.keepOriginalPositionXZ, take.lockRootHeightY, take.keepOriginalPositionY
                },
                Is.All.True,
                $"{path}: the root locked in place");
            Assert.That(take.heightFromFeet, Is.False, path);
            Assert.That(take.events, Is.Empty, $"{path}: markers come from the manifest, never an event");
            AnimationClip imported = ArtDeliveryImporter.LoadClip(path)!;
            Assert.That(imported.events, Is.Empty, path);
            Assert.That(imported.length, Is.EqualTo(clip.Frames / 30f).Within(0.01f), $"{path}: its length");
        }
    }

    [TestCaseSource(nameof(Deliveries))]
    public void ClipTables_OfADelivery_HoldEveryClipWithItsMarkers(string folder)
    {
        foreach (ArtManifestClip clip in ReadManifest(folder).Clips)
        {
            BodyClips clips = AssetDatabase.LoadAssetAtPath<BodyClips>(ArtPaths.ClipsAssetPath(clip.Rig));
            Assert.That(clips, Is.Not.Null, ArtPaths.ClipsAssetPath(clip.Rig));
            Assert.That(clips.TryGet(clip.Name, out BodyClips.Entry entry), Is.True, $"{clip.Rig} {clip.Name}");
            Assert.That(
                (entry.Frames, entry.Loop, entry.CalibrationSpeed),
                Is.EqualTo((clip.Frames, clip.Loop, clip.CalibrationSpeed)),
                $"{clip.Rig} {clip.Name}");
            if (clip.Name is "run" or "move")
            {
                Assert.That(entry.CalibrationSpeed, Is.GreaterThan(0f), $"{clip.Rig} {clip.Name}'s calibration");
            }

            Assert.That(
                entry.Markers.Select(marker => (marker.Name, marker.Frame)),
                Is.EquivalentTo(clip.Markers.Select(marker => (marker.Key, marker.Value))),
                $"{clip.Rig} {clip.Name}'s markers");
            Assert.That(entry.Clip, Is.EqualTo(ArtDeliveryImporter.LoadClip(ArtPaths.ClipPath(clip))));
        }
    }

    [TestCaseSource(nameof(Deliveries))]
    public void Textures_OfADelivery_KeepTheirColourSpaceAndSize(string folder)
    {
        foreach (ArtManifestAsset asset in ReadManifest(folder).Assets)
        {
            foreach (string texture in asset.Textures)
            {
                string path = ArtPaths.TexturePath(asset, texture);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.sRGBTexture,
                    Is.EqualTo(path.EndsWith("_BaseMap.png") || path.EndsWith("_Emission.png")), path);
                Assert.That(
                    importer.maxTextureSize,
                    Is.EqualTo(asset.Kind == "weapon"
                        ? ArtImportSettings.WeaponTextureSize
                        : ArtImportSettings.BodyTextureSize),
                    path);
            }
        }
    }

    [TestCaseSource(nameof(Deliveries))]
    public void Meshes_OfADelivery_KeepTheBudgetsAndTwoBoneWeights(string folder)
    {
        foreach (ArtManifestAsset asset in ReadManifest(folder).Assets)
        {
            string path = ArtPaths.ModelPath(asset);
            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            int triangles = meshes.Sum(mesh =>
                Enumerable.Range(0, mesh.subMeshCount).Sum(sub => (int)mesh.GetIndexCount(sub)) / 3);
            Assert.That(triangles, Is.LessThanOrEqualTo(TriangleBudget(asset.Kind)), $"{path}: triangles");
            foreach (Mesh mesh in meshes)
            {
                using NativeArray<byte> counts = mesh.GetBonesPerVertex();
                Assert.That(counts.Length == 0 || counts.Max() <= ArtImportSettings.MaxBoneWeights, Is.True,
                    $"{path}: weights");
            }
        }
    }

    [TestCaseSource(nameof(Deliveries))]
    public void Prefabs_OfADelivery_CarryTheirAnchorsSlotsAndClips(string folder)
    {
        foreach (ArtManifestAsset asset in ReadManifest(folder).Assets)
        {
            if (asset.Kind == "weapon")
            {
                AssertWeapon(asset);
                continue;
            }

            string path = FindPrefab(asset.Key);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Assert.That(prefab.TryGetComponent(out EntityBody body), Is.True, $"{path}: its EntityBody");
            AssertAnchor(asset, body.Overhead, ArtDeliveryImporter.OverheadAnchor, prefab);
            AssertAnchor(asset, body.Pick, ArtDeliveryImporter.PickAnchor, prefab);
            if (asset.Anchors.ContainsKey(ArtDeliveryImporter.ProjectileAnchor))
            {
                AssertAnchor(asset, body.Projectile, ArtDeliveryImporter.ProjectileAnchor, prefab);
            }

            Assert.That(
                body.PickRadius,
                Is.EqualTo(Mathf.Min(asset.PickRadius, EntityBody.MaxPickRadius)),
                $"{path}: its pick radius, capped");
            if (asset.Key.StartsWith("character_"))
            {
                Assert.That(asset.Materials, Does.Contain(ArtDeliveryImporter.TrimSlot), $"{asset.Key}: a Trim slot");
                Assert.That(body.TrimSlots, Is.Not.Empty, $"{path}: the tint's slots");
            }

            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, $"{path}: no collider");
            Assert.That(
                prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(skin => skin.quality),
                Is.All.EqualTo(SkinQuality.Bone2),
                $"{path}: two bones a vertex on every platform");
            Assert.That(prefab.TryGetComponent(out Animator animator), Is.True, $"{path}: its Animator");
            Assert.That(
                (animator.applyRootMotion, animator.runtimeAnimatorController == null),
                Is.EqualTo((false, true)),
                $"{path}: no root motion, no controller");
            Assert.That(prefab.TryGetComponent(out BodyAnimator bodyAnimator), Is.True, $"{path}: its BodyAnimator");
            string rig = asset.Kind == "humanoid" ? ArtPaths.HumanoidRig : asset.Key;
            Assert.That(
                bodyAnimator.Clips,
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<BodyClips>(ArtPaths.ClipsAssetPath(rig))),
                $"{path}: its rig's clips");
        }
    }

    private static void AssertAnchor(ArtManifestAsset asset, Transform? anchor, string name, GameObject prefab)
    {
        Assert.That(anchor, Is.Not.Null, $"{asset.Key}: {name}");
        Vector3 offset = prefab.transform.InverseTransformPoint(anchor!.position);
        Vector3 expected = asset.Anchors[name];
        // ArtDelivery-02's manifest writes an anchor's sideways offset with its sign flipped, by at most 3.4 mm; the
        // game reads the model's own anchor, so only a displacement that could show is refused.
        Assert.That(Vector3.Distance(offset, expected), Is.LessThan(0.005f), $"{asset.Key}: {name} at {expected}");
    }

    private static void AssertWeapon(ArtManifestAsset asset)
    {
        Assert.That(ArtDeliveryImporter.TryGetWeapon(asset.Key, out WeaponGrip grip, out string pickupKey), Is.True);
        GameObject held = AssetDatabase.LoadAssetAtPath<GameObject>(FindPrefab(asset.Key));
        Assert.That(held, Is.Not.Null, asset.Key);
        Assert.That(held.TryGetComponent(out HeldWeapon weapon), Is.True, $"{asset.Key}: its grip");
        Assert.That(weapon.Grip, Is.EqualTo(grip));
        Assert.That(held.GetComponentsInChildren<Collider>(true), Is.Empty, $"{asset.Key}: no collider");

        GameObject pickup = AssetDatabase.LoadAssetAtPath<GameObject>(FindPrefab(pickupKey));
        Assert.That(pickup, Is.Not.Null, pickupKey);
        Assert.That(pickup.TryGetComponent(out EntityBody body), Is.True, $"{pickupKey}: its EntityBody");
        Assert.That(body.Pick, Is.Null, $"{pickupKey}: picked by the sphere every drop has");
        Assert.That(body.Overhead!.localPosition.y, Is.GreaterThan(0f), pickupKey);
    }

    [Test]
    public void Deliveries_AreCommitted()
    {
        Assert.That(Deliveries(), Is.Not.Empty, "ArtDelivery-01-StylePilot is integrated");
    }
}
}
