using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Evertorch.Client.Editor
{
public static class RunServerToolbarButton
{
    public static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    public static string ScriptPath => Path.Combine(RepositoryRoot, "scripts", "run-server.cmd");

    [MainToolbarElement("Evertorch/Run Server", defaultDockPosition = MainToolbarDockPosition.Middle)]
    public static MainToolbarElement Create()
    {
        var icon = EditorGUIUtility.IconContent("d_PlayButton").image as Texture2D;
        var content = new MainToolbarContent(
            "Run Server",
            icon,
            "Build content and start a local Development server (scripts/run-server.cmd)");
        return new MainToolbarButton(content, Launch)
            { enabled = Application.platform == RuntimePlatform.WindowsEditor };
    }

    private static void Launch()
    {
        string script = ScriptPath;
        if (!File.Exists(script))
        {
            Debug.LogError($"Run Server: {script} was not found.");
            return;
        }

        // A separate console window keeps the server alive across domain reloads, Play Mode, and editor restarts.
        Process.Start(new ProcessStartInfo(script) { UseShellExecute = true, WorkingDirectory = RepositoryRoot });
    }
}
}
