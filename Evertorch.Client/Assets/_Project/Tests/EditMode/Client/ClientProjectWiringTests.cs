using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
/// Checks on project assets the client code relies on by name, so a rename fails here instead of at runtime.
/// </summary>
[TestFixture]
public sealed class ClientProjectWiringTests
{
    private const string ActionsPath = "Assets/_Project/Settings/InputSystem_Actions.inputactions";

    [Test]
    public void MapSceneResolver_KnownKey_NamesASceneThatIsInTheBuild()
    {
        bool found = MapSceneResolver.TryResolve("map_training_ground", out string sceneName);

        string[] buildScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => System.IO.Path.GetFileNameWithoutExtension(scene.path))
            .ToArray();
        Assert.That(found, Is.True);
        Assert.That(buildScenes, Does.Contain(sceneName));
        Assert.That(buildScenes.First(), Is.EqualTo("00_Bootstrap"), "the client starts in the bootstrap scene");
    }

    [Test]
    public void MapSceneResolver_UnknownKey_IsRefused()
    {
        Assert.That(MapSceneResolver.TryResolve("map_nowhere", out string sceneName), Is.False);
        Assert.That(sceneName, Is.Empty);
    }

    [TestCase("Player/Move")]
    [TestCase("Player/MoveTo")]
    public void InputActions_HaveTheActionsTheClientBinds(string actionPath)
    {
        Assert.That(LoadActions().FindAction(actionPath), Is.Not.Null);
    }

    [Test]
    public void InputActions_MoveIsSharedByKeyboardGamepadAndTheOnScreenStick()
    {
        InputAction move = LoadActions().FindAction("Player/Move", true);

        string[] paths = move.bindings.Select(binding => binding.path).ToArray();
        Assert.That(paths, Does.Contain("<Keyboard>/w"));
        Assert.That(paths, Does.Contain(TouchControls.StickControlPath), "the on-screen stick drives this control");
    }

    [Test]
    public void InputActions_MoveToIsSharedByMouseClickAndTouchTap()
    {
        InputAction moveTo = LoadActions().FindAction("Player/MoveTo", true);

        string[] paths = moveTo.bindings.Select(binding => binding.path).ToArray();
        Assert.That(paths, Is.EquivalentTo(new[] { "<Mouse>/leftButton", "<Touchscreen>/touch*/tap" }));
    }

    [Test]
    public void LiteNetLib_IsUsedOnlyByTheClientTransportAdapter()
    {
        string root = System.IO.Path.Combine(UnityEngine.Application.dataPath, "_ProjectScripts.Evertorch.Client");
        Regex usesLibrary = new Regex(@"LiteNetLib\s*[;.]");

        string[] files = System.IO.Directory
            .GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories)
            .Where(file => usesLibrary.IsMatch(System.IO.File.ReadAllText(file)))
            .Select(file => System.IO.Path.GetFileName(file))
            .ToArray();

        Assert.That(files, Is.EqualTo(new[] { "LiteNetLibClientTransport.cs" }));
    }

    private static InputActionAsset LoadActions()
    {
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        Assert.That(asset, Is.Not.Null, ActionsPath);
        return asset;
    }
}
}
