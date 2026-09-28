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
        BuildReport report = Build(BuildOptions.Development | BuildOptions.AutoRunPlayer);
        Debug.Log($"Web build {report.summary.result} in {OutputPath} ({report.summary.totalErrors} errors).");
    }

    public static void BuildFromCommandLine()
    {
        BuildReport report = Build(BuildOptions.Development);
        Debug.Log($"Web build {report.summary.result} in {OutputPath} ({report.summary.totalErrors} errors).");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static BuildReport Build(BuildOptions options)
    {
        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        BuildTargetGroup previousGroup = BuildPipeline.GetBuildTargetGroup(previousTarget);
        InsecureHttpOption previousHttp = PlayerSettings.insecureHttpOption;
        bool previousFallback = PlayerSettings.WebGL.decompressionFallback;
        try
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            PlayerSettings.WebGL.decompressionFallback = true;
            return BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path)
                        .ToArray(),
                    locationPathName = OutputPath,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = options
                });
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
