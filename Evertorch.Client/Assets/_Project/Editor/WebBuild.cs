using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Evertorch.Client.Editor
{
/// <summary>
///     A development web build in <c>artifacts/client/Web</c> (Coding Standards §10): from the menu, which also runs
///     it in a browser as Build And Run does, or in batch mode with the editor closed through
///     <c>-executeMethod Evertorch.Client.Editor.WebBuild.BuildFromCommandLine</c>. For the build only, it lets a
///     development build fetch over plain HTTP, since its page and its <c>StreamingAssets</c> are served that way
///     locally, and turns on the decompression fallback, so any static file server can serve it; the project's
///     settings and build target are put back afterwards.
/// </summary>
public static class WebBuild
{
    public const string OutputFolder = "artifacts/client/Web";

    private static string OutputPath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", OutputFolder));

    [MenuItem("Evertorch/Build Web")]
    public static void BuildFromMenu()
    {
        Build(BuildOptions.Development | BuildOptions.AutoRunPlayer);
    }

    public static void BuildFromCommandLine()
    {
        EditorApplication.Exit(Build(BuildOptions.Development) ? 0 : 1);
    }

    private static bool Build(BuildOptions options)
    {
        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        BuildTargetGroup previousGroup = BuildPipeline.GetBuildTargetGroup(previousTarget);
        InsecureHttpOption previousHttp = PlayerSettings.insecureHttpOption;
        bool previousFallback = PlayerSettings.WebGL.decompressionFallback;
        try
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            PlayerSettings.WebGL.decompressionFallback = true;

            // Addressables, built with the player, places its bundles by the active target, not the player's: built
            // from another target, the page's catalog would point at bundles the build never copied.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL
                && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Debug.LogError("Web build not started: the project could not switch to WebGL.");
                return false;
            }

            BuildReport report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path)
                        .ToArray(),
                    locationPathName = OutputPath,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = options
                });
            Debug.Log($"Web build {report.summary.result} in {OutputPath} ({report.summary.totalErrors} errors).");
            return report.summary.result == BuildResult.Succeeded;
        }
        finally
        {
            PlayerSettings.insecureHttpOption = previousHttp;
            PlayerSettings.WebGL.decompressionFallback = previousFallback;
            if (EditorUserBuildSettings.activeBuildTarget != previousTarget)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(previousGroup, previousTarget);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
}
