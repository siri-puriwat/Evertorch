using System.Diagnostics;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Evertorch.Client.Editor
{
public static class TerminalToolbarButton
{
    // The run guide's commands, its docker compose ones included, are written to run from the repository root.
    public static string WorkingDirectory => RunServerToolbarButton.RepositoryRoot;

    [MainToolbarElement("Evertorch/Terminal", defaultDockPosition = MainToolbarDockPosition.Middle)]
    public static MainToolbarElement Create()
    {
        var icon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image as Texture2D;
        var content = new MainToolbarContent(
            "Terminal",
            icon,
            "Open a Command Prompt at the repository root, for the run guide's commands (docker compose, scripts)");
        return new MainToolbarButton(content, Open)
            { enabled = Application.platform == RuntimePlatform.WindowsEditor };
    }

    private static void Open()
    {
        Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = WorkingDirectory });
    }
}
}
