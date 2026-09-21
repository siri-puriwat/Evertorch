using UnityEngine;
using UnityEngine.SceneManagement;

namespace Evertorch.Client
{
/// <summary>
/// Every client starts in the bootstrap scene, which owns the connection and loads the map scene the server names.
/// Play can still begin in a map scene: one left open in the editor, or a Multiplayer Play Mode window, which starts
/// in whatever scene the main editor shows at that moment. Such a client would sit in an empty map with nothing
/// connected, so it is sent to the bootstrap first.
/// </summary>
public static class BootstrapRedirect
{
    public const string BootstrapScene = "00_Bootstrap";

    public static bool ShouldRedirect(string activeSceneName, bool hasGameClient)
    {
        return !hasGameClient && MapSceneResolver.IsMapScene(activeSceneName);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RedirectFromMapScene()
    {
        bool hasGameClient = Object.FindFirstObjectByType<GameClient>() != null;
        if (ShouldRedirect(SceneManager.GetActiveScene().name, hasGameClient))
        {
            SceneManager.LoadScene(BootstrapScene);
        }
    }
}
}
