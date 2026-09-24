using UnityEngine;
using UnityEngine.SceneManagement;

namespace Evertorch.Client
{
/// <summary>
///     Every client starts in the bootstrap scene, which owns the connection and loads the map scene the server names.
///     Play can still begin in a map or the main menu scene: one left open in the editor, or a Multiplayer Play Mode
///     window, which starts in whatever scene the main editor shows at that moment. Such a client would sit there with
///     nothing connected, so it is sent to the bootstrap first.
/// </summary>
public static class BootstrapRedirect
{
    public const string BootstrapScene = "00_Bootstrap";

    /// <summary>
    ///     What a client shows while it is out of the world after being in it: a camera and a light, with the client's
    ///     own interface over them.
    /// </summary>
    public const string MainMenuScene = "01_MainMenu";

    public static bool ShouldRedirect(string activeSceneName, bool hasGameClient)
    {
        return !hasGameClient
            && (MapSceneResolver.IsMapScene(activeSceneName) || activeSceneName == MainMenuScene);
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
