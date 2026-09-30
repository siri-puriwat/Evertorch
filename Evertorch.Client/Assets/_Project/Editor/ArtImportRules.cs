using UnityEditor;

namespace Evertorch.Client.Editor
{
/// <summary>
///     Gives a newly delivered model, clip, or texture its import settings (Content Pipeline §6). It touches only a
///     file that has no settings yet: once the <c>.meta</c> file holds them, a reimport, as in a fresh clone, keeps
///     them, so a clip never loses the avatar it copies while that avatar is still importing.
/// </summary>
public sealed class ArtImportRules : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!ArtPaths.IsArtAsset(assetPath) || !assetImporter.importSettingsMissing)
        {
            return;
        }

        ArtImportSettings.ApplyModel((ModelImporter)assetImporter, assetPath.Contains("@"));
    }

    private void OnPreprocessTexture()
    {
        if (!ArtPaths.IsArtAsset(assetPath) || !assetImporter.importSettingsMissing)
        {
            return;
        }

        ArtImportSettings.ApplyTexture((TextureImporter)assetImporter, assetPath);
    }
}
}
