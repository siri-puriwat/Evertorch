using System.Collections;
using System.Collections.Generic;
using System.IO;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
/// Simulated devices drive the project's real input actions and the client's real input sources. Whatever the
/// device, the only thing that comes out is a <see cref="MoveIntent"/> from the one producer.
/// </summary>
public sealed class SharedIntentPathTests : InputTestFixture
{
    private const float TickSeconds = 0.05f;
    private const float Speed = 5f;
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";

    // North row first; one-metre cells with the origin at zero.
    private static readonly string[] Yard =
    {
        "##########",
        "#........#",
        "#........#",
        "#...##...#",
        "#...##...#",
        "#........#",
        "#........#",
        "##########",
    };

    private readonly List<Object> m_created = new List<Object>();
    private Rig? m_rig;

    public override void Setup()
    {
        base.Setup();
    }

    public override void TearDown()
    {
        m_rig?.Dispose();
        m_rig = null;

        // Destroyed now, not at the end of the frame: an on-screen stick removes its virtual gamepad when it is
        // disabled, and that has to happen while this test's input state is still the active one.
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
    public void Keyboard_ProducesAUnitIntent()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Rig rig = CreateRig();

        SetKeys(keyboard, Key.W, Key.D);
        rig.Tick();

        MoveIntent intent = rig.Sent[0];
        Assert.That(rig.Sent.Count, Is.EqualTo(1));
        Assert.That(intent.DirectionX, Is.EqualTo(0.7071f).Within(1e-3f));
        Assert.That(intent.DirectionZ, Is.EqualTo(0.7071f).Within(1e-3f));
    }

    [Test]
    public void GamepadStick_ProducesTheSameKindOfIntentAtFullSpeedWhateverTheTilt()
    {
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Rig rig = CreateRig();

        SetStick(gamepad, new Vector2(0.5f, 0f));
        rig.Tick();

        Assert.That(rig.Sent.Count, Is.EqualTo(1));
        Assert.That(rig.Sent[0].DirectionX, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(rig.Sent[0].DirectionZ, Is.EqualTo(0f).Within(1e-4f));
    }

    [UnityTest]
    public IEnumerator OnScreenStick_DraggedByATouch_ProducesAnIntentThroughTheMoveAction()
    {
        // The stick is moved by the UI event system, which ignores input while the application is unfocused unless
        // told otherwise, and test runs are usually driven without the Game view having focus.
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        TouchControls controls = rig.CreateTouchControls();
        yield return null;
        Vector2 knob = RectTransformUtility.WorldToScreenPoint(null, controls.Knob!.position);

        BeginTouch(1, knob, screen: touchscreen);
        yield return null;
        MoveTouch(1, knob + new Vector2(0f, 300f), screen: touchscreen);

        // One frame for the event system to drag the knob, and one for the stick's own event to be processed.
        yield return null;
        yield return null;
        rig.Tick();

        Assert.That(
            rig.Sent.Count,
            Is.EqualTo(1),
            $"the stick moved the Move action; knob offset {controls.Knob.anchoredPosition}");
        Assert.That(rig.Sent[0].DirectionX, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(rig.Sent[0].DirectionZ, Is.EqualTo(1f).Within(1e-3f));
        Assert.That(rig.Controller.HasPath, Is.False, "touching the stick is not a tap on the ground");

        EndTouch(1, knob + new Vector2(0f, 300f), screen: touchscreen);
        yield return null;
        yield return null;
        rig.Tick();
        rig.Tick();

        Assert.That(rig.Sent[rig.Sent.Count - 1].DirectionX, Is.EqualTo(0f));
        Assert.That(rig.Sent[rig.Sent.Count - 1].DirectionZ, Is.EqualTo(0f), "releasing the stick sends a stop");
        Assert.That(rig.Controller.HasPath, Is.False);
    }

    [UnityTest]
    public IEnumerator MouseClick_OnTheGround_StartsAWalkToThatPoint()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        yield return null;
        WorldPosition target = new WorldPosition(7.5f, 0f, 5.5f);

        ClickAt(mouse, rig.ScreenPointOf(target));
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.Accepted));
        Assert.That(rig.Controller.HasPath, Is.True);
        AssertNear(rig.Destination, target);
        Assert.That(rig.Sent.Count, Is.EqualTo(1), "the walk produced this tick's intent");
    }

    [UnityTest]
    public IEnumerator TouchTap_OnTheGround_StartsTheSameWalkAsAClick()
    {
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        yield return null;
        WorldPosition target = new WorldPosition(7.5f, 0f, 5.5f);
        Vector2 screenPoint = rig.ScreenPointOf(target);

        BeginTouch(1, screenPoint, screen: touchscreen);
        EndTouch(1, screenPoint, screen: touchscreen);
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.Accepted));
        AssertNear(rig.Destination, target);
        Assert.That(rig.Sent.Count, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator TouchTap_OnTheOnScreenStick_IsNotAGroundTap()
    {
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        TouchControls controls = rig.CreateTouchControls();
        yield return null;
        Vector2 onStick = RectTransformUtility.WorldToScreenPoint(null, controls.StickArea!.position);

        BeginTouch(1, onStick, screen: touchscreen);
        EndTouch(1, onStick, screen: touchscreen);
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.OnControl));
        Assert.That(rig.Controller.HasPath, Is.False);
    }

    [UnityTest]
    public IEnumerator Click_OnTheDevelopmentOverlay_IsNotAGroundClick()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        DevelopmentOverlay overlay = rig.CreateOverlay();
        yield return null;

        ClickAt(mouse, CenterOf(overlay.Panel!));
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.OnControl));
        Assert.That(rig.Controller.HasPath, Is.False);
    }

    [UnityTest]
    public IEnumerator F1_HidesTheDevelopmentOverlay_SoItNoLongerTakesClicks()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        DevelopmentOverlay overlay = rig.CreateOverlay();
        yield return null;
        Vector2 onPanel = CenterOf(overlay.Panel!);

        // Queued without a manual update: the overlay polls "pressed this frame", so the press has to arrive in the
        // input update of the frame whose Update reads it.
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
        yield return null;
        SetKeys(keyboard);
        ClickAt(mouse, onPanel);
        PointerMoveResult result = rig.Tick();

        Assert.That(overlay.IsVisible, Is.False);
        Assert.That(result, Is.Not.EqualTo(PointerMoveResult.OnControl));
    }

    [UnityTest]
    public IEnumerator OverlayButton_WhenTapped_IsNotLeftSelected()
    {
        // A selected control receives the UI navigate action, which shares WASD and the gamepad stick with movement.
        // The tap goes through the UI event system, which ignores an unfocused application unless told otherwise.
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        DevelopmentOverlay overlay = rig.CreateOverlay();
        yield return null;
        Button[] buttons = overlay.GetComponentsInChildren<Button>();
        Assert.That(buttons.Length, Is.EqualTo(1), "only Connect shows while disconnected");
        bool isClicked = false;
        buttons[0].onClick.AddListener(() => isClicked = true);
        Vector2 onButton = CenterOf((RectTransform)buttons[0].transform);

        BeginTouch(1, onButton, screen: touchscreen);
        yield return null;
        EndTouch(1, onButton, screen: touchscreen);
        yield return null;
        yield return null;

        Assert.That(isClicked, Is.True, "the tap reached the button");
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
    }

    [UnityTest]
    public IEnumerator Click_OnAWall_IsRefusedAndSendsNothing()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        yield return null;

        ClickAt(mouse, rig.ScreenPointOf(new WorldPosition(4.5f, GrayboxMeshBuilder.WallHeight, 3.5f)));
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.Refused));
        Assert.That(rig.Controller.RejectedMoveRequests, Is.EqualTo(1));
        Assert.That(rig.Sent, Is.Empty, "an unreachable click produces no movement command");
    }

    [UnityTest]
    public IEnumerator Click_OutsideTheMap_DoesNothing()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        yield return null;

        ClickAt(mouse, rig.ScreenPointOf(new WorldPosition(60f, 0f, 60f)));
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.MissedMap));
        Assert.That(rig.Sent, Is.Empty);
    }

    [UnityTest]
    public IEnumerator KeyDuringAWalk_CancelsItInTheSameClientTick()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Rig rig = CreateRig();
        yield return null;
        ClickAt(mouse, rig.ScreenPointOf(new WorldPosition(7.5f, 0f, 5.5f)));
        rig.Tick();
        MoveIntent walking = rig.Sent[0];

        SetKeys(keyboard, Key.S);
        rig.Tick();

        MoveIntent manual = rig.Sent[1];
        Assert.That(walking.DirectionX, Is.GreaterThan(0.5f), "the walk was heading east");
        Assert.That(manual.DirectionX, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(manual.DirectionZ, Is.EqualTo(-1f).Within(1e-4f), "the very next intent is the key's");
        Assert.That(rig.Controller.HasPath, Is.False);
        Assert.That(manual.Sequence, Is.EqualTo(walking.Sequence + 1), "no tick in between");
    }

    [UnityTest]
    public IEnumerator StickDuringAWalk_CancelsItInTheSameClientTick()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Rig rig = CreateRig();
        yield return null;
        ClickAt(mouse, rig.ScreenPointOf(new WorldPosition(7.5f, 0f, 5.5f)));
        rig.Tick();

        SetStick(gamepad, new Vector2(-1f, 0f));
        rig.Tick();

        Assert.That(rig.Sent[1].DirectionX, Is.EqualTo(-1f).Within(1e-4f));
        Assert.That(rig.Controller.HasPath, Is.False);

        SetStick(gamepad, Vector2.zero);
        rig.Tick();
        rig.Tick();

        Assert.That(rig.Controller.HasPath, Is.False, "letting go does not resume the walk");
        Assert.That(rig.Sent[rig.Sent.Count - 1].DirectionX, Is.EqualTo(0f));
    }

    [UnityTest]
    public IEnumerator EveryScheme_FeedsOneProducer_SoSequencesNeverRepeatOrSkip()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        yield return null;

        SetKeys(keyboard, Key.D);
        rig.Tick();
        SetKeys(keyboard);
        SetStick(gamepad, new Vector2(0f, 1f));
        rig.Tick();
        SetStick(gamepad, Vector2.zero);
        ClickAt(mouse, rig.ScreenPointOf(new WorldPosition(7.5f, 0f, 5.5f)));
        rig.Tick();
        rig.Tick();

        Assert.That(rig.Sent.Count, Is.EqualTo(4));
        for (int index = 0; index < rig.Sent.Count; index++)
        {
            Assert.That(rig.Sent[index].Sequence, Is.EqualTo((uint)(index + 1)));
        }
    }

    // Devices are driven with complete state events, the way hardware reports, in one input update each. The
    // fixture's single-control helpers send delta events, which stopped reaching a device here once a real frame
    // had run; whole states do not have that problem and are closer to real input anyway.
    private static void SetKeys(Keyboard keyboard, params Key[] pressed)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));
        InputSystem.Update();
    }

    private static void SetStick(Gamepad gamepad, Vector2 leftStick)
    {
        InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = leftStick });
        InputSystem.Update();
    }

    private static void ClickAt(Mouse mouse, Vector2 screenPosition)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPosition }.WithButton(MouseButton.Left));
        InputSystem.Update();
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPosition });
        InputSystem.Update();
    }

    // Input is bound once the devices exist, which is also the order the client binds it in.
    private Rig CreateRig()
    {
        m_rig = new Rig(m_created);
        return m_rig;
    }

    private static Vector2 CenterOf(RectTransform rect)
    {
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }

    private static void AssertNear(WorldPosition actual, WorldPosition expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.05f));
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.05f));
    }

    private sealed class Rig : IMoveIntentSink
    {
        private readonly List<Object> m_created;
        private readonly ManualMoveSource m_manual;
        private readonly PointerMoveSource m_pointer;
        private readonly LocalPlayerDriver m_driver;
        private readonly ClientWorld m_world;
        private readonly Camera m_camera;
        private readonly GrayboxMap m_map;
        private PointerMoveHandler m_handler;
        private uint m_tick;

        public Rig(List<Object> created)
        {
            m_created = created;
            string actionsPath = Path.Combine(Application.dataPath, ActionsAsset);
            if (!File.Exists(actionsPath))
            {
                Assert.Ignore("The input actions asset is only readable from the editor project.");
            }

            InputActionAsset actions = InputActionAsset.FromJson(File.ReadAllText(actionsPath));
            m_created.Add(actions);
            m_manual = new ManualMoveSource(actions.FindAction("Player/Move", true));
            m_pointer = new PointerMoveSource(actions.FindAction("Player/MoveTo", true));
            m_handler = new PointerMoveHandler(m_pointer, null, null);

            NavigationGrid grid = CreateGrid();
            WorldEntered entered = new WorldEntered(
                new MapDefinitionId("map.training_ground"),
                1,
                new EntityId(100),
                0,
                new WorldPosition(2.5f, 0f, 5.5f),
                new WorldDirection(0f, 1f),
                Speed);
            m_world = new ClientWorld(grid, entered, (uint)Mathf.RoundToInt(1f / TickSeconds));
            Controller = new MovementController(grid);
            m_driver = new LocalPlayerDriver(Controller, new MoveIntentProducer(), m_world, this);

            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m_created.Add(material);
            m_map = GrayboxMap.Create(grid, material);
            m_created.Add(m_map.gameObject);

            GameObject cameraObject = new GameObject("TestCamera");
            m_created.Add(cameraObject);
            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.transform.position = new Vector3(5f, 30f, 4f);
            m_camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }

        public MovementController Controller { get; }

        public List<MoveIntent> Sent { get; } = new List<MoveIntent>();

        public WorldPosition Destination => Controller.Path[Controller.Path.Count - 1];

        public void Send(MoveIntent intent)
        {
            Sent.Add(intent);
        }

        public TouchControls CreateTouchControls()
        {
            TouchControls controls = TouchControls.Create();
            m_created.Add(controls.gameObject);
            m_handler = new PointerMoveHandler(m_pointer, controls, null);
            return controls;
        }

        public DevelopmentOverlay CreateOverlay()
        {
            // Never activated, so the client neither loads content nor connects; the overlay only reads its state.
            GameObject clientObject = new GameObject("TestClient");
            clientObject.SetActive(false);
            m_created.Add(clientObject);
            DevelopmentOverlay overlay = DevelopmentOverlay.Create(clientObject.AddComponent<GameClient>());
            m_created.Add(overlay.gameObject);
            m_handler = new PointerMoveHandler(m_pointer, null, overlay);
            return overlay;
        }

        public Vector2 ScreenPointOf(WorldPosition position)
        {
            return m_camera.WorldToScreenPoint(new Vector3(position.X, position.Y, position.Z));
        }

        /// <summary>
        /// One client frame in the order <see cref="GameClient"/> runs it: held direction, pointer request, tick.
        /// </summary>
        public PointerMoveResult Tick()
        {
            m_manual.Apply(Controller, 0f);
            PointerMoveResult result = m_handler.Handle(
                m_camera,
                m_map.GroundCollider,
                Controller,
                m_world.Predictor.Position,
                out WorldPosition _);
            m_tick++;
            m_driver.Tick(m_tick);
            return result;
        }

        public void Dispose()
        {
            m_pointer.Dispose();
        }

        private static NavigationGrid CreateGrid()
        {
            int rows = Yard.Length;
            int columns = Yard[0].Length;
            NavigationCell[] cells = new NavigationCell[rows * columns];
            for (int row = 0; row < rows; row++)
            {
                string text = Yard[rows - 1 - row];
                for (int column = 0; column < columns; column++)
                {
                    NavigationSurface surface = text[column] == '.' ? NavigationSurface.Floor : NavigationSurface.Wall;
                    cells[(row * columns) + column] = NavigationCell.Level(surface, 0f);
                }
            }

            return new NavigationGrid(columns, rows, 1f, 0f, 0f, 0.3f, 0.4f, cells);
        }
    }
}
}
