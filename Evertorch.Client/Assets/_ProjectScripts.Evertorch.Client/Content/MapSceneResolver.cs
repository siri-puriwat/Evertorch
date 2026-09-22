namespace Evertorch.Client
{
/// <summary>
///     Content names a map's scene by a stable key; this is the one place that knows which Unity scene that is.
/// </summary>
public static class MapSceneResolver
{
    private const string TrainingGroundScene = "10_TrainingGround";

    public static bool TryResolve(string sceneKey, out string sceneName)
    {
        switch (sceneKey)
        {
            case "map_training_ground":
                sceneName = TrainingGroundScene;
                return true;
            default:
                sceneName = string.Empty;
                return false;
        }
    }

    public static bool IsMapScene(string sceneName)
    {
        return sceneName == TrainingGroundScene;
    }
}
}
