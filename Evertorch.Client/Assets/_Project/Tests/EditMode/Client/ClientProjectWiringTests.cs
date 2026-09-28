using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Checks on project assets the client code relies on by name, so a rename fails here instead of at runtime.
/// </summary>
[TestFixture]
public sealed class ClientProjectWiringTests
{
    private const string ActionsPath = "Assets/_Project/Settings/InputSystem_Actions.inputactions";

    [TestCase("10_TrainingGround", false, true)]
    [TestCase("10_TrainingGround", true, false)]
    [TestCase("11_TrainingField", false, true)]
    [TestCase("11_TrainingField", true, false)]
    [TestCase("01_MainMenu", false, true)]
    [TestCase("01_MainMenu", true, false)]
    [TestCase("00_Bootstrap", false, false)]
    [TestCase("InitTestScene637000000000000000", false, false)]
    public void BootstrapRedirect_OnlyAMapOrMainMenuSceneWithoutAClientGoesBackToTheBootstrap(
        string activeScene,
        bool hasGameClient,
        bool expected)
    {
        Assert.That(BootstrapRedirect.ShouldRedirect(activeScene, hasGameClient), Is.EqualTo(expected));
    }

    [TestCase("Player/Move")]
    [TestCase("Player/MoveTo")]
    [TestCase("Player/Look")]
    [TestCase("Player/Orbit")]
    [TestCase("Player/Zoom")]
    [TestCase("Player/ZoomStep")]
    [TestCase("Player/Slot1")]
    [TestCase("Player/Slot2")]
    [TestCase("Player/Slot3")]
    [TestCase("Player/Slot4")]
    [TestCase("Player/Slot5")]
    [TestCase("Player/Talk")]
    [TestCase("Player/Stats")]
    public void InputActions_HaveTheActionsTheClientBinds(string actionPath)
    {
        Assert.That(LoadActions().FindAction(actionPath), Is.Not.Null);
    }

    [TestCase("Player/Jump")]
    [TestCase("Player/Crouch")]
    [TestCase("Player/Sprint")]
    public void InputActions_HaveNoTemplateActionsVersionOneDoesNotUse(string actionPath)
    {
        Assert.That(LoadActions().FindAction(actionPath), Is.Null);
    }

    private static string[] Paths(InputActionAsset actions, string actionPath)
    {
        return actions.FindAction(actionPath, true).bindings.Select(binding => binding.path).ToArray();
    }

    private static InputActionAsset LoadActions()
    {
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        Assert.That(asset, Is.Not.Null, ActionsPath);
        return asset;
    }

    [TestCase("map_training_ground", "10_TrainingGround")]
    [TestCase("map_training_field", "11_TrainingField")]
    public void MapSceneResolver_KnownKey_NamesItsMapScene(string sceneKey, string expected)
    {
        bool found = MapSceneResolver.TryResolve(sceneKey, out string sceneName);

        Assert.That(found, Is.True);
        Assert.That(sceneName, Is.EqualTo(expected));
        Assert.That(MapSceneResolver.IsMapScene(sceneName), Is.True);
    }

    [Test]
    public void BootstrapScene_IsTheFirstSceneInTheBuild()
    {
        string first = Path.GetFileNameWithoutExtension(EditorBuildSettings.scenes[0].path);

        Assert.That(first, Is.EqualTo(BootstrapRedirect.BootstrapScene));
    }

    [Test]
    public void InputActions_CameraFollowsThePrototypeControls()
    {
        InputActionAsset actions = LoadActions();

        Assert.That(
            Paths(actions, "Player/Look"),
            Is.EquivalentTo(new[] { "<Gamepad>/rightStick", "<Joystick>/{Hatswitch}" }),
            "no pointer delta, which would orbit the camera on every mouse move");
        Assert.That(
            Paths(actions, "Player/Orbit"),
            Is.EquivalentTo(new[] { "OneModifier", "<Mouse>/rightButton", "<Mouse>/delta" }));
        Assert.That(Paths(actions, "Player/Zoom"), Is.EquivalentTo(new[] { "<Mouse>/scroll/y" }));
        Assert.That(Paths(actions, "Player/ZoomStep"), Is.EquivalentTo(new[] { "<Gamepad>/rightStickPress" }));
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
    public void InputActions_SkillSlotsFollowThePrototypeControls()
    {
        InputActionAsset actions = LoadActions();

        Assert.That(
            Paths(actions, "Player/Slot1"),
            Is.EquivalentTo(new[] { "<Keyboard>/1", "<Gamepad>/buttonSouth" }));
        Assert.That(
            Paths(actions, "Player/Slot2"),
            Is.EquivalentTo(new[] { "<Keyboard>/2", "<Gamepad>/leftTrigger" }));
        Assert.That(
            Paths(actions, "Player/Slot3"),
            Is.EquivalentTo(new[] { "<Keyboard>/3", "<Gamepad>/rightTrigger" }));
        Assert.That(
            Paths(actions, "Player/Slot4"),
            Is.EquivalentTo(new[] { "<Keyboard>/4", "<Gamepad>/dpad/down" }));
        Assert.That(
            Paths(actions, "Player/Slot5"),
            Is.EquivalentTo(new[] { "<Keyboard>/5", "<Gamepad>/dpad/up" }));
        Assert.That(SkillSlots.Count, Is.EqualTo(5), "one action per slot of the bar");
    }

    [Test]
    public void InputActions_TargetingFollowsThePrototypeControls()
    {
        InputActionAsset actions = LoadActions();

        Assert.That(Paths(actions, "Player/Attack"), Is.EquivalentTo(new[] { "<Gamepad>/buttonWest" }));
        Assert.That(
            Paths(actions, "Player/Next"),
            Is.EquivalentTo(new[] { "<Keyboard>/tab", "<Gamepad>/rightShoulder" }));
        Assert.That(
            Paths(actions, "Player/Previous"),
            Is.EquivalentTo(new[] { "OneModifier", "<Keyboard>/shift", "<Keyboard>/tab", "<Gamepad>/leftShoulder" }));
        Assert.That(
            Paths(actions, "Player/ClearTarget"),
            Is.EquivalentTo(new[] { "<Keyboard>/escape", "<Gamepad>/buttonEast" }));
        Assert.That(Paths(actions, "Player/Respawn"), Is.EquivalentTo(new[] { "<Keyboard>/r", "<Gamepad>/start" }));
        Assert.That(
            Paths(actions, "Player/Pickup"),
            Is.EquivalentTo(new[] { "<Keyboard>/f", "<Gamepad>/buttonNorth" }));
        Assert.That(actions.FindAction("Player/Pickup", true).interactions, Is.Empty, "a press, not a hold");
        Assert.That(
            Paths(actions, "Player/Talk"),
            Is.EquivalentTo(new[] { "<Keyboard>/e", "<Gamepad>/dpad/right" }));
        Assert.That(actions.FindAction("Player/Talk", true).interactions, Is.Empty, "a press, not a hold");
        Assert.That(actions.FindAction("Player/Interact"), Is.Null, "the template's Interact is replaced");
    }

    [Test]
    public void InputActions_TheWindowsFollowThePrototypeControls()
    {
        InputActionAsset actions = LoadActions();

        Assert.That(
            Paths(actions, "Player/Stats"),
            Is.EquivalentTo(new[] { "<Keyboard>/c", TouchControls.StatsControlPath }),
            "the Stats touch button drives the gamepad's Select");
        Assert.That(TouchControls.StatsControlPath, Is.EqualTo("<Gamepad>/select"));
        Assert.That(actions.FindAction("Player/Stats", true).interactions, Is.Empty, "a press, not a hold");
    }

    [Test]
    public void LiteNetLib_IsUsedOnlyByTheClientTransportAdapter()
    {
        string root = Path.Combine(Application.dataPath, "_ProjectScripts.Evertorch.Client");
        var usesLibrary = new Regex(@"LiteNetLib\s*[;.]");

        string[] files = Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => usesLibrary.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetFileName(file))
            .ToArray();

        Assert.That(files, Is.EqualTo(new[] { "LiteNetLibClientTransport.cs" }));
    }

    [Test]
    public void MainMenuScene_ComesRightAfterTheBootstrapInTheBuild()
    {
        string[] buildScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => Path.GetFileNameWithoutExtension(scene.path))
            .ToArray();

        Assert.That(buildScenes, Has.Length.GreaterThan(1));
        Assert.That(buildScenes[1], Is.EqualTo(BootstrapRedirect.MainMenuScene));
        Assert.That(MapSceneResolver.IsMapScene(BootstrapRedirect.MainMenuScene), Is.False);
    }

    [Test]
    public void MapSceneResolver_KnownKey_NamesASceneThatIsInTheBuild()
    {
        bool found = MapSceneResolver.TryResolve("map_training_ground", out string sceneName);

        string[] buildScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => Path.GetFileNameWithoutExtension(scene.path))
            .ToArray();
        Assert.That(found, Is.True);
        Assert.That(buildScenes, Does.Contain(sceneName));
        Assert.That(buildScenes.First(), Is.EqualTo("00_Bootstrap"), "the client starts in the bootstrap scene");
    }

    [Test]
    public void MapSceneResolver_ResolvedScene_IsAMapScene()
    {
        MapSceneResolver.TryResolve("map_training_ground", out string sceneName);

        Assert.That(MapSceneResolver.IsMapScene(sceneName), Is.True);
        Assert.That(MapSceneResolver.IsMapScene(BootstrapRedirect.BootstrapScene), Is.False);
    }

    [Test]
    public void MapSceneResolver_UnknownKey_IsRefused()
    {
        Assert.That(MapSceneResolver.TryResolve("map_nowhere", out string sceneName), Is.False);
        Assert.That(sceneName, Is.Empty);
    }

    [Test]
    public void MapScenes_FollowOneAnotherInTheBuild_TheFieldAfterTheGround()
    {
        string[] buildScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => Path.GetFileNameWithoutExtension(scene.path))
            .ToArray();

        int ground = Array.IndexOf(buildScenes, "10_TrainingGround");
        Assert.That(ground, Is.GreaterThan(1));
        Assert.That(buildScenes[ground + 1], Is.EqualTo("11_TrainingField"));
    }
}
}
