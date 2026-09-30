using UnityEditor;

namespace Evertorch.Client.Editor
{
/// <summary>
///     The import settings of delivered art (Content Pipeline §6), those the delivery's own Unity validation used. The
///     committed <c>.meta</c> files hold them; <see cref="ArtImportRules" /> gives a new file the settings that need no
///     other asset, and <see cref="ArtDeliveryImporter" /> the rest.
/// </summary>
public static class ArtImportSettings
{
    public const int MaxBoneWeights = 2;

    // The delivery asked for 0.000001; Unity keeps no weight below 0.001, so that is what its validation had too.
    public const float MinBoneWeight = 0.001f;
    public const int BodyTextureSize = 1024;
    public const int WeaponTextureSize = 512;

    public static void ApplyModel(ModelImporter importer, bool importsAnimation)
    {
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.preserveHierarchy = true;
        importer.isReadable = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.importNormals = ModelImporterNormals.Import;
        importer.skinWeights = ModelImporterSkinWeights.Custom;
        importer.maxBonesPerVertex = MaxBoneWeights;
        importer.minBoneWeight = MinBoneWeight;
        importer.optimizeGameObjects = false;
        importer.importAnimation = importsAnimation;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.importCameras = false;
        importer.importLights = false;
    }

    public static void ApplyTexture(TextureImporter importer, string assetPath)
    {
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = !assetPath.EndsWith("_MaskMap.png") && !assetPath.EndsWith("_Normal.png");
        if (assetPath.EndsWith("_Normal.png"))
        {
            importer.textureType = TextureImporterType.NormalMap;
        }

        importer.mipmapEnabled = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.maxTextureSize = assetPath.Contains("/weapon_") ? WeaponTextureSize : BodyTextureSize;
        importer.textureCompression = TextureImporterCompression.Compressed;
    }
}
}
