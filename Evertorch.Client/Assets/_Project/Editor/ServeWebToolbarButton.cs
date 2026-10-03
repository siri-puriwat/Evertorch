using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Evertorch.Client.Editor
{
public static class ServeWebToolbarButton
{
    public static string ScriptPath => Path.Combine(RepositoryRoot, "scripts", "serve-web.cmd");

    private static string RepositoryRoot => RunServerToolbarButton.RepositoryRoot;

    [MainToolbarElement("Evertorch/Serve Web", defaultDockPosition = MainToolbarDockPosition.Middle)]
    public static MainToolbarElement Create()
    {
        var icon = EditorGUIUtility.IconContent("d_BuildSettings.Web.Small").image as Texture2D;
        var content = new MainToolbarContent(
            "Serve Web",
            icon,
            "Serve the web build in the browser (scripts/serve-web.cmd); make it first with Evertorch > Build Web");
        return new MainToolbarButton(content, Launch)
            { enabled = Application.platform == RuntimePlatform.WindowsEditor };
    }

    private static void Launch()
    {
        string script = ScriptPath;
        if (!File.Exists(script))
        {
            Debug.LogError($"Serve Web: {script} was not found.");
            return;
        }

        // Its own console window keeps the web server alive across domain reloads and editor restarts.
        Process.Start(new ProcessStartInfo(script) { UseShellExecute = true, WorkingDirectory = RepositoryRoot });
    }
}
}
