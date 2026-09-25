using System.Collections;
using System.Collections.Generic;
using System.IO;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The camera through the project's real input actions (Prototype Content §3, §4): a right-button drag, the right
///     stick, the wheel, a press of the stick, a two-finger drag, and a pinch move only the camera, never the player;
///     and the map pulls the camera in when it stands between the player and the camera.
/// </summary>
public sealed class CameraTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const float Tolerance = 1e-3f;

    private readonly List<Object> m_created = new();
    private CameraInputSource? m_camera;
    private PointerMoveSource? m_pointer;
    private OrbitCameraState m_state = null!;

    public override void Setup()
    {
        base.Setup();
        string actionsPath = Path.Combine(Application.dataPath, ActionsAsset);
        if (!File.Exists(actionsPath))
        {
            Assert.Ignore("The input actions asset is only readable from the editor project.");
        }

        var actions = InputActionAsset.FromJson(File.ReadAllText(actionsPath));
        m_created.Add(actions);
        var uiHitTest = new UiHitTest();
        m_camera = new CameraInputSource(
            actions.FindAction("Player/Look", true),
            actions.FindAction("Player/Orbit", true),
            actions.FindAction("Player/Zoom", true),
            actions.FindAction("Player/ZoomStep", true),
            uiHitTest);
        m_pointer = new PointerMoveSource(actions.FindAction("Player/MoveTo", true), m_camera.IsGestureTap);
        m_state = new OrbitCameraState(90f, 30f, 60f, 6f, 20f);
    }

    public override void TearDown()
    {
        m_camera?.Dispose();
        m_camera = null;
        m_pointer?.Dispose();
        m_pointer = null;

        // Destroyed now, while this test's input state is still the active one (see SharedIntentPathTests).
        for (int index = m_created.Count - 1; index >= 0; index--)
        {
            if (m_created[index] != null)
            {
                Object.DestroyImmediate(m_created[index]);
            }
        }

        m_created.Clear();
        base.TearDown();
    }

    [Test]
    public void MouseMove_WithoutTheRightButton_DoesNotOrbit()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();

        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(50f, -25f) });
        InputSystem.Update();
        m_camera!.Apply(m_state, 0f);

        Assert.That(m_state.YawDegrees, Is.EqualTo(0f), "Player/Look has no pointer delta any more");
        Assert.That(m_state.PitchDegrees, Is.EqualTo(OrbitCameraState.DefaultPitchDegrees));
    }

    [Test]
    public void RightButtonDrag_Orbits_AndNeverWalks()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();

        InputSystem.QueueStateEvent(
            mouse,
            new MouseState { delta = new Vector2(50f, -25f) }.WithButton(MouseButton.Right));
        InputSystem.Update();
        m_camera!.Apply(m_state, 0f);

        Assert.That(m_state.YawDegrees, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(m_state.PitchDegrees, Is.EqualTo(OrbitCameraState.DefaultPitchDegrees - 5f).Within(Tolerance));
        Assert.That(m_pointer!.TryTakeRequest(out Vector2 _), Is.False, "a right-button drag is no click");
    }

    [Test]
    public void Wheel_ZoomsIn()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();

        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0f, 1f) });
        InputSystem.Update();
        m_camera!.Apply(m_state, 0f);

        Assert.That(m_state.Distance, Is.LessThan(OrbitCameraState.DefaultDistance));
        Assert.That(m_pointer!.TryTakeRequest(out Vector2 _), Is.False);
    }

    [Test]
    public void RightStick_Orbits_AndItsPressStepsThroughThreeDistances()
    {
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();

        InputSystem.QueueStateEvent(gamepad, new GamepadState { rightStick = new Vector2(1f, 0f) });
        InputSystem.Update();
        m_camera!.Apply(m_state, 0.5f);
        Assert.That(m_state.YawDegrees, Is.EqualTo(45f).Within(Tolerance), "90° a second for half a second");

        float[] expected = { 12f, 6f, OrbitCameraState.DefaultDistance };
        foreach (float distance in expected)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.RightStick));
            InputSystem.Update();
            m_camera.Apply(m_state, 0f);
            Assert.That(m_state.Distance, Is.EqualTo(distance).Within(Tolerance));
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.Update();
            m_camera.Apply(m_state, 0f);
        }
    }

    [UnityTest]
    public IEnumerator TwoFingerDrag_Orbits_AndPinch_Zooms_WithoutAWalk()
    {
        Touchscreen screen = InputSystem.AddDevice<Touchscreen>();
        var first = new Vector2(300f, 300f);
        var second = new Vector2(500f, 300f);

        BeginTouch(1, first, screen: screen);
        BeginTouch(2, second, screen: screen);
        yield return null;
        m_camera!.Apply(m_state, 0f);

        MoveTouch(1, first + new Vector2(40f, 0f), screen: screen);
        MoveTouch(2, second + new Vector2(40f, 0f), screen: screen);
        yield return null;
        m_camera.Apply(m_state, 0f);
        Assert.That(m_state.YawDegrees, Is.EqualTo(8f).Within(Tolerance), "the fingers' midpoint moved 40 pixels");

        // Twice the span: fingers moving apart halve the distance.
        MoveTouch(1, first + new Vector2(-60f, 0f), screen: screen);
        MoveTouch(2, second + new Vector2(140f, 0f), screen: screen);
        yield return null;
        m_camera.Apply(m_state, 0f);
        Assert.That(m_state.Distance, Is.EqualTo(OrbitCameraState.DefaultDistance / 2f).Within(Tolerance));

        EndTouch(1, first + new Vector2(-60f, 0f), screen: screen);
        EndTouch(2, second + new Vector2(140f, 0f), screen: screen);
        yield return null;
        m_camera.Apply(m_state, 0f);
        Assert.That(m_pointer!.TryTakeRequest(out Vector2 _), Is.False, "a two-finger gesture never walks");
    }

    [UnityTest]
    public IEnumerator QuickTwoFingerTap_NeverWalks([Values(true, false)] bool isFirstLiftedFirst)
    {
        Touchscreen screen = InputSystem.AddDevice<Touchscreen>();
        var first = new Vector2(300f, 300f);
        var second = new Vector2(500f, 300f);

        BeginTouch(1, first, screen: screen);
        BeginTouch(2, second, screen: screen);
        yield return null;
        m_camera!.Apply(m_state, 0f);
        if (isFirstLiftedFirst)
        {
            EndTouch(1, first, screen: screen);
            EndTouch(2, second, screen: screen);
        }
        else
        {
            EndTouch(2, second, screen: screen);
            EndTouch(1, first, screen: screen);
        }

        yield return null;
        m_camera.Apply(m_state, 0f);

        Assert.That(m_pointer!.TryTakeRequest(out Vector2 _), Is.False);
    }

    [UnityTest]
    public IEnumerator OneFingerTap_StillWalks()
    {
        Touchscreen screen = InputSystem.AddDevice<Touchscreen>();
        var tap = new Vector2(400f, 300f);

        BeginTouch(1, tap, screen: screen);
        EndTouch(1, tap, screen: screen);
        yield return null;
        m_camera!.Apply(m_state, 0f);

        Assert.That(m_pointer!.TryTakeRequest(out Vector2 position), Is.True, "a lone tap is a walk request");
        Assert.That(position, Is.EqualTo(tap));
    }

    [UnityTest]
    public IEnumerator TapWhileAFingerHoldsTheStick_StillWalks()
    {
        Touchscreen screen = InputSystem.AddDevice<Touchscreen>();
        var controls = TouchControls.Create();
        m_created.Add(controls.gameObject);
        controls.SetVisible(true);
        yield return null;
        Vector2 onStick = RectTransformUtility.WorldToScreenPoint(null, controls.StickArea!.position);
        var tap = new Vector2(Screen.width * 0.6f, Screen.height * 0.6f);

        BeginTouch(1, onStick, screen: screen);
        yield return null;
        m_camera!.Apply(m_state, 0f);
        BeginTouch(2, tap, screen: screen);
        EndTouch(2, tap, screen: screen);
        yield return null;
        m_camera.Apply(m_state, 0f);

        Assert.That(m_pointer!.TryTakeRequest(out Vector2 _), Is.True, "the stick finger is the UI's, not a gesture");
        EndTouch(1, onStick, screen: screen);
    }

    [UnityTest]
    public IEnumerator FollowCamera_WithAGateBetweenHeadAndCamera_PullsIn_AndEasesBackOut()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m_created.Add(material);
        var map = GrayboxMap.Create(CreateGatedYard(), material);
        m_created.Add(map.gameObject);
        var player = new GameObject("Player");
        m_created.Add(player);
        player.transform.position = new Vector3(11.4f, 0f, 10f);
        var cameraObject = new GameObject("FollowCameraUnderTest");
        m_created.Add(cameraObject);
        cameraObject.AddComponent<Camera>();
        FollowCamera follow = cameraObject.AddComponent<FollowCamera>();

        // The camera east of the player, low: the 2.6 m gate a metre east of the player is in the way.
        m_state.Orbit(-90f, -30f);
        follow.Follow(player.transform, m_state, map.GroundCollider);
        yield return null;
        float full = FullDistance(player.transform.position);
        float shown = Vector3.Distance(cameraObject.transform.position, Probe(player.transform.position));
        Assert.That(shown, Is.LessThan(1f), $"pulled in from {full} m");

        // West, over open floor: nothing is in the way, and the camera eases back out.
        m_state.Orbit(180f, 0f);
        yield return new WaitForSecondsRealtime(0.4f);
        m_state.GetOffset(out float x, out float y, out float z);
        Vector3 wanted = player.transform.position + new Vector3(x, y, z);
        Assert.That(Vector3.Distance(cameraObject.transform.position, wanted), Is.LessThan(Tolerance));
    }

    private static Vector3 Probe(Vector3 feet)
    {
        return feet + Vector3.up * 2f;
    }

    private float FullDistance(Vector3 feet)
    {
        m_state.GetOffset(out float x, out float y, out float z);
        return Vector3.Distance(feet + new Vector3(x, y, z), Probe(feet));
    }

    // Twenty by twenty metres of floor with a gate across the whole yard at x 12–13.
    private static NavigationGrid CreateGatedYard()
    {
        const int size = 20;
        var cells = new NavigationCell[size * size];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                NavigationSurface surface = column == 12 ? NavigationSurface.Gate : NavigationSurface.Floor;
                cells[row * size + column] = NavigationCell.Level(surface, 0f);
            }
        }

        return new NavigationGrid(size, size, 1f, 0f, 0f, 0.3f, 0.4f, cells);
    }
}
}
