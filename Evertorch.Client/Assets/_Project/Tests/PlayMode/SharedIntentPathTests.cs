using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     Simulated devices drive the project's real input actions and the client's real input sources. Whatever the
///     device, the only thing that comes out is a <see cref="MoveIntent" /> from the one producer.
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
        "##########"
    };

    private readonly List<Object> m_created = new();
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

    // While the chat input has focus no gameplay key acts: W, A, S, D, 1, and 2 do nothing, and once it closes they act
    // again (Prototype Content §4; critique finding 16 of the pre-Milestone-12 review).
    [Test]
    public void GameplayKeys_WhileTheChatGateIsShut_DoNothing_AndActOnceItOpens()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Rig rig = CreateRig();
        SkillInputSource skills = rig.CreateSkillSource();
        var gate = new PlayerInputGate(rig.Actions);
        object chat = new();

        gate.Shut(chat);
        foreach (Key key in new[] { Key.W, Key.A, Key.S, Key.D })
        {
            SetKeys(keyboard, key);
            rig.Tick();
            SetKeys(keyboard);
        }

        Press(keyboard.digit1Key);
        Release(keyboard.digit1Key);
        Press(keyboard.digit2Key);
        Release(keyboard.digit2Key);
        int slotWhileShut = skills.TakeSlot();
        int sentWhileShut = rig.Sent.Count;
        WorldPosition stood = rig.World.Predictor.Position;
        gate.Open(chat);
        SetKeys(keyboard, Key.W);
        rig.Tick();
        SetKeys(keyboard);
        Press(keyboard.digit2Key);
        Release(keyboard.digit2Key);

        Assert.That(gate.IsShut, Is.False);
        Assert.That((sentWhileShut, slotWhileShut), Is.EqualTo((0, 0)), "nothing moved and no slot was asked for");
        Assert.That(stood, Is.EqualTo(new WorldPosition(2.5f, 0f, 5.5f)));
        Assert.That(rig.Sent, Has.Count.EqualTo(1), "W walks again");
        Assert.That(skills.TakeSlot(), Is.EqualTo(2), "2 asks for its slot again");
    }

    [Test]
    public void Keyboard_WhileDead_ProducesNoMovement()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Rig rig = CreateRig();
        rig.World.OnEntityDied(new EntityDied(rig.World.LocalEntity, default, 1));

        SetKeys(keyboard, Key.W);
        rig.Tick();

        Assert.That(rig.Sent, Is.Empty);
        Assert.That(rig.World.Predictor.Position, Is.EqualTo(new WorldPosition(2.5f, 0f, 5.5f)));
    }

    [Test]
    public void RespawnKeyAndStartButton_AskToRespawn()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Rig rig = CreateRig();
        CombatInputSource source = rig.CreateCombatSource();

        Press(keyboard.rKey);
        CombatRequest fromKey = source.TakeRequest();
        Release(keyboard.rKey);
        Press(gamepad.startButton);
        CombatRequest fromButton = source.TakeRequest();

        Assert.That(fromKey, Is.EqualTo(CombatRequest.Respawn));
        Assert.That(fromButton, Is.EqualTo(CombatRequest.Respawn));
        Assert.That(source.TakeRequest(), Is.EqualTo(CombatRequest.None));
    }

    [Test]
    public void PickupKeyAndNorthButton_AskToPickUp()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Rig rig = CreateRig();
        CombatInputSource source = rig.CreateCombatSource();

        Press(keyboard.fKey);
        CombatRequest fromKey = source.TakeRequest();
        Release(keyboard.fKey);
        Press(gamepad.buttonNorth);
        CombatRequest fromButton = source.TakeRequest();

        Assert.That(fromKey, Is.EqualTo(CombatRequest.Pickup));
        Assert.That(fromButton, Is.EqualTo(CombatRequest.Pickup));
        Assert.That(source.TakeRequest(), Is.EqualTo(CombatRequest.None));
    }

    // D-pad left pages South and the triggers to slots 6 to 8 and back; the keys 1 to 8 always ask for their own slot
    // (Prototype Content §4).
    [Test]
    public void DpadLeft_PagesTheGamepadsSkillButtons_ButNeverTheKeys()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Rig rig = CreateRig();
        SkillInputSource source = rig.CreateSkillSource();
        var asked = new List<int>();

        Press(gamepad.buttonSouth);
        Release(gamepad.buttonSouth);
        asked.Add(source.TakeSlot());
        Press(gamepad.dpad.left);
        Release(gamepad.dpad.left);
        bool isTurned = source.TakePageToggle();
        source.IsOnOwnSkills = true;
        foreach (ButtonControl button in new[] { gamepad.buttonSouth, gamepad.leftTrigger, gamepad.rightTrigger })
        {
            Press(button);
            Release(button);
            asked.Add(source.TakeSlot());
        }

        foreach (KeyControl key in new[] { keyboard.digit1Key, keyboard.digit6Key, keyboard.digit8Key })
        {
            Press(key);
            Release(key);
            asked.Add(source.TakeSlot());
        }

        Assert.That(isTurned, Is.True);
        Assert.That(source.TakePageToggle(), Is.False, "taken once");
        Assert.That(asked, Is.EqualTo(new[] { 1, 6, 7, 8, 1, 6, 8 }));
    }

    // A gamepad has no pointer to choose a skill's target with, so the source says which device asked (owner's walk,
    // 2026-09-30).
    [Test]
    public void SkillSource_SaysWhetherAGamepadAsked()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Rig rig = CreateRig();
        SkillInputSource source = rig.CreateSkillSource();

        Press(gamepad.buttonSouth);
        Release(gamepad.buttonSouth);
        int fromPad = source.TakeSlot(out bool isPad);
        Press(keyboard.digit6Key);
        Release(keyboard.digit6Key);
        int fromKey = source.TakeSlot(out bool isKeyPad);
        int none = source.TakeSlot(out bool isNonePad);

        Assert.That((fromPad, isPad), Is.EqualTo((1, true)));
        Assert.That((fromKey, isKeyPad), Is.EqualTo((6, false)));
        Assert.That((none, isNonePad), Is.EqualTo((0, false)));
    }

    // Out of the world the page and a turn asked for there are forgotten, so the next entry starts on slots 1 to 3
    // (review of Milestone 10).
    [Test]
    public void SkillSource_Reset_ForgetsThePageAndATurnAskedFor()
    {
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Rig rig = CreateRig();
        SkillInputSource source = rig.CreateSkillSource();
        source.IsOnOwnSkills = true;
        Press(gamepad.dpad.left);
        Release(gamepad.dpad.left);

        source.Reset();
        Press(gamepad.buttonSouth);
        Release(gamepad.buttonSouth);

        Assert.That(source.TakePageToggle(), Is.False);
        Assert.That(source.IsOnOwnSkills, Is.False);
        Assert.That(source.TakeSlot(), Is.EqualTo(1));
    }

    [Test]
    public void TalkKeyAndDpadRight_AskToTalk()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Rig rig = CreateRig();
        CombatInputSource source = rig.CreateCombatSource();

        Press(keyboard.eKey);
        CombatRequest fromKey = source.TakeRequest();
        Release(keyboard.eKey);
        Press(gamepad.dpad.right);
        CombatRequest fromButton = source.TakeRequest();

        Assert.That(fromKey, Is.EqualTo(CombatRequest.Talk));
        Assert.That(fromButton, Is.EqualTo(CombatRequest.Talk));
        Assert.That(source.TakeRequest(), Is.EqualTo(CombatRequest.None));
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
        var target = new WorldPosition(7.5f, 0f, 5.5f);

        ClickAt(mouse, rig.ScreenPointOf(target));
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.Accepted));
        Assert.That(rig.Controller.HasPath, Is.True);
        AssertNear(rig.Destination, target);
        Assert.That(rig.Sent.Count, Is.EqualTo(1), "the walk produced this tick's intent");
    }

    // While a skill waits for its target a click on the ground walks nowhere; the client ends the wait instead
    // (owner's walk, 2026-09-30).
    [UnityTest]
    public IEnumerator MouseClick_OnTheGround_WhileASkillWaits_WalksNowhere()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        yield return null;

        ClickAt(mouse, rig.ScreenPointOf(new WorldPosition(7.5f, 0f, 5.5f)));
        PointerMoveResult result = rig.Tick(false);

        Assert.That(result, Is.EqualTo(PointerMoveResult.OnGround));
        Assert.That(rig.Controller.HasPath, Is.False);
    }

    [UnityTest]
    public IEnumerator MouseClick_OnAMonster_PicksItInsteadOfWalking()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        yield return null;
        var slime = new WorldPosition(7.5f, 0f, 5.5f);
        rig.Entities.Add(new PickCandidate(new EntityId(7), slime));

        ClickAt(mouse, rig.ScreenPointOf(new WorldPosition(slime.X, EntityPicker.PickHeight, slime.Z)));
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.Entity));
        Assert.That(rig.Picked, Is.EqualTo(new EntityId(7)));
        Assert.That(rig.Controller.HasPath, Is.False, "the ground under the monster is not walked to");
        Assert.That(rig.Sent, Is.Empty);
    }

    [UnityTest]
    public IEnumerator TouchTap_OnTheGround_StartsTheSameWalkAsAClick()
    {
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        yield return null;
        var target = new WorldPosition(7.5f, 0f, 5.5f);
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

        // Shown as F1 would.
        overlay.Panel!.gameObject.SetActive(true);
        yield return null;

        ClickAt(mouse, CenterOf(Child(overlay, "Connection")));
        PointerMoveResult result = rig.Tick();

        Assert.That(result, Is.EqualTo(PointerMoveResult.OnControl));
        Assert.That(rig.Controller.HasPath, Is.False);
    }

    [UnityTest]
    public IEnumerator F1_ShowsTheHiddenDevelopmentOverlay_AndHidesItAgain()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Rig rig = CreateRig();
        DevelopmentOverlay overlay = rig.CreateOverlay();
        yield return null;
        bool isHiddenAtFirst = !overlay.IsVisible;

        // Queued without a manual update: the overlay polls "pressed this frame", so the press has to arrive in the
        // input update of the frame whose Update reads it.
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
        yield return null;
        SetKeys(keyboard);
        yield return null;
        bool isShown = overlay.IsVisible;

        // On the panel, away from its buttons.
        Vector2 onPanel = CenterOf(Child(overlay, "Connection"));
        ClickAt(mouse, onPanel);
        PointerMoveResult whileShown = rig.Tick();

        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
        yield return null;
        SetKeys(keyboard);
        ClickAt(mouse, onPanel);
        PointerMoveResult afterHiding = rig.Tick();

        Assert.That(isHiddenAtFirst, Is.True, "the panels a player sees come first");
        Assert.That(isShown, Is.True);
        Assert.That(whileShown, Is.EqualTo(PointerMoveResult.OnControl));
        Assert.That(overlay.IsVisible, Is.False);
        Assert.That(afterHiding, Is.Not.EqualTo(PointerMoveResult.OnControl));
    }

    [UnityTest]
    public IEnumerator TouchTargetButtons_AskForWhatTheirGamepadButtonsAskFor()
    {
        // The buttons are pressed through the UI event system, which ignores an unfocused application unless told
        // otherwise.
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        TouchControls controls = rig.CreateTouchControls();
        CombatInputSource source = rig.CreateCombatSource();
        yield return null;

        var requests = new List<CombatRequest>();
        foreach (string button in new[] { "Next", "Previous", "Clear" })
        {
            Vector2 onButton = CenterOf(Child(controls, button));
            BeginTouch(1, onButton, screen: touchscreen);

            // One frame for the event system to press the button, and one for the button's own event.
            yield return null;
            yield return null;
            requests.Add(source.TakeRequest());
            EndTouch(1, onButton, screen: touchscreen);
            yield return null;
            yield return null;
        }

        Assert.That(requests, Is.EqualTo(new[] { CombatRequest.Next, CombatRequest.Previous, CombatRequest.Clear }));
    }

    // The Stats touch button drives the gamepad's Select and the Skills button a press of the left stick, so one action
    // each serves the key, the gamepad, and the button (Prototype Content §4).
    [UnityTest]
    public IEnumerator WindowTouchButtons_AskForTheirWindows_AsTheirGamepadControlsDo()
    {
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        TouchControls controls = rig.CreateTouchControls();
        WindowInputSource source = rig.CreateWindowSource();
        yield return null;

        Vector2 onButton = CenterOf(Child(controls, "Stats"));
        BeginTouch(1, onButton, screen: touchscreen);
        yield return null;
        yield return null;
        bool pressed = source.TakeStatsToggle();
        bool skillsOnStats = source.TakeSkillsToggle();
        EndTouch(1, onButton, screen: touchscreen);
        yield return null;
        yield return null;
        bool again = source.TakeStatsToggle();
        Vector2 onSkills = CenterOf(Child(controls, "Skills"));
        BeginTouch(2, onSkills, screen: touchscreen);
        yield return null;
        yield return null;
        bool skills = source.TakeSkillsToggle();
        bool statsOnSkills = source.TakeStatsToggle();
        EndTouch(2, onSkills, screen: touchscreen);
        yield return null;
        yield return null;

        Assert.That(pressed, Is.True, "the Stats button asks for its window");
        Assert.That(again, Is.False, "once per press");
        Assert.That(skills, Is.True, "the Skills button asks for its window");
        Assert.That((skillsOnStats, statsOnSkills), Is.EqualTo((false, false)), "each button asks for its own");
    }

    [UnityTest]
    public IEnumerator DevButton_ShowsTheDevelopmentOverlay_AndTheOverlaysHideButtonHidesIt()
    {
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        DevelopmentOverlay overlay = rig.CreateOverlay();
        TouchControls controls = rig.CreateTouchControls(overlay.Toggle);
        yield return null;

        yield return Tap(touchscreen, CenterOf(Child(controls, "Dev")));
        Assert.That(overlay.IsVisible, Is.True, "the Dev button shows the overlay");
        yield return Tap(touchscreen, CenterOf(Child(overlay, "Hide")));

        Assert.That(overlay.IsVisible, Is.False);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
    }

    [UnityTest]
    public IEnumerator DevButton_StaysWhenTheOverlayHidesTheStick_SoATouchDeviceCanShowTheOverlayAgain()
    {
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        Rig rig = CreateRig();
        DevelopmentOverlay overlay = rig.CreateOverlay();
        TouchControls controls = rig.CreateTouchControls(overlay.Toggle);
        yield return null;

        controls.SetVisible(false);
        yield return null;
        RectTransform dev = controls.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == "Dev");
        Assert.That(controls.IsVisible, Is.False, "the stick is hidden");
        Assert.That(dev.gameObject.activeInHierarchy, Is.True, "the Dev button stays");
        yield return Tap(touchscreen, CenterOf(dev));

        Assert.That(overlay.IsVisible, Is.True, "and still shows the overlay");
    }

    [UnityTest]
    public IEnumerator LoginButton_WhenTapped_IsNotLeftSelected()
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
        LoginPanel login = rig.CreateLoginPanel();
        yield return null;
        Button[] buttons = login.GetComponentsInChildren<Button>();
        Assert.That(buttons.Length, Is.EqualTo(1), "only Connect shows while disconnected");

        // Only the tap is under test; a real connect would open a socket nothing closes.
        bool isClicked = false;
        buttons[0].onClick.RemoveAllListeners();
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

    private static RectTransform Child(Component root, string objectName)
    {
        return root.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == objectName);
    }

    private IEnumerator Tap(Touchscreen touchscreen, Vector2 position)
    {
        BeginTouch(1, position, screen: touchscreen);
        yield return null;
        EndTouch(1, position, screen: touchscreen);
        yield return null;
        yield return null;
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
        private readonly InputActionAsset m_actions;
        private CombatInputSource? m_combat;
        private WindowInputSource? m_windows;
        private SkillInputSource? m_skills;
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

            var actions = InputActionAsset.FromJson(File.ReadAllText(actionsPath));
            m_created.Add(actions);
            m_actions = actions;
            m_manual = new ManualMoveSource(actions.FindAction("Player/Move", true));
            m_pointer = new PointerMoveSource(actions.FindAction("Player/MoveTo", true));
            m_handler = new PointerMoveHandler(m_pointer, null);

            NavigationGrid grid = CreateGrid();
            var entered = new WorldEntered(
                new MapDefinitionId("map.training_ground"),
                1,
                new EntityId(100),
                new JobDefinitionId("job.adventurer"),
                0,
                new WorldPosition(2.5f, 0f, 5.5f),
                new WorldDirection(0f, 1f),
                Speed,
                71,
                71,
                1.5f,
                0,
                new CharacterId(1),
                1,
                0,
                30,
                24,
                24);
            m_world = new ClientWorld(grid, entered, (uint)Mathf.RoundToInt(1f / TickSeconds));
            Controller = new MovementController(grid);
            m_driver = new LocalPlayerDriver(Controller, new MoveIntentProducer(), m_world, this);

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m_created.Add(material);
            m_map = GrayboxMap.Create(grid, material);
            m_created.Add(m_map.gameObject);

            var cameraObject = new GameObject("TestCamera");
            m_created.Add(cameraObject);
            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.transform.position = new Vector3(5f, 30f, 4f);
            m_camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }

        public MovementController Controller { get; }

        public ClientWorld World => m_world;

        public InputActionAsset Actions => m_actions;

        public List<MoveIntent> Sent { get; } = new();

        public List<PickCandidate> Entities { get; } = new();

        public EntityId Picked { get; private set; }

        public WorldPosition Destination => Controller.Path[Controller.Path.Count - 1];

        public void Send(MoveIntent intent)
        {
            Sent.Add(intent);
        }

        public TouchControls CreateTouchControls(UnityAction? toggleOverlay = null)
        {
            var controls = TouchControls.Create(toggleOverlay);
            m_created.Add(controls.gameObject);
            m_handler = new PointerMoveHandler(m_pointer, new UiHitTest());
            return controls;
        }

        public CombatInputSource CreateCombatSource()
        {
            m_combat = new CombatInputSource(
                m_actions.FindAction("Player/Next", true),
                m_actions.FindAction("Player/Previous", true),
                m_actions.FindAction("Player/ClearTarget", true),
                m_actions.FindAction("Player/Attack", true),
                m_actions.FindAction("Player/Respawn", true),
                m_actions.FindAction("Player/Pickup", true),
                m_actions.FindAction("Player/Talk", true));
            return m_combat;
        }

        public SkillInputSource CreateSkillSource()
        {
            var slots = new List<InputAction>();
            for (int slot = 1; slot <= SkillSlots.Count; slot++)
            {
                slots.Add(m_actions.FindAction($"Player/Slot{slot}", true));
            }

            m_skills = new SkillInputSource(slots, m_actions.FindAction("Player/SkillPage", true));
            return m_skills;
        }

        public WindowInputSource CreateWindowSource()
        {
            m_windows = new WindowInputSource(
                m_actions.FindAction("Player/Stats", true),
                m_actions.FindAction("Player/Skills", true));
            return m_windows;
        }

        public DevelopmentOverlay CreateOverlay()
        {
            var overlay = DevelopmentOverlay.Create(CreateIdleClient());
            m_created.Add(overlay.gameObject);
            m_handler = new PointerMoveHandler(m_pointer, new UiHitTest());
            return overlay;
        }

        public LoginPanel CreateLoginPanel()
        {
            var login = LoginPanel.Create(CreateIdleClient());
            m_created.Add(login.gameObject);
            m_handler = new PointerMoveHandler(m_pointer, new UiHitTest());
            return login;
        }

        // Never activated, so the client neither loads content nor connects; a panel only reads its state.
        private GameClient CreateIdleClient()
        {
            var clientObject = new GameObject("TestClient");
            clientObject.SetActive(false);
            m_created.Add(clientObject);
            return clientObject.AddComponent<GameClient>();
        }

        public Vector2 ScreenPointOf(WorldPosition position)
        {
            return m_camera.WorldToScreenPoint(new Vector3(position.X, position.Y, position.Z));
        }

        /// <summary>
        ///     One client frame in the order <see cref="GameClient" /> runs it: held direction, pointer request, tick.
        /// </summary>
        public PointerMoveResult Tick(bool isWalking = true)
        {
            m_manual.Apply(Controller, 0f);
            PointerMoveResult result = m_handler.Handle(
                m_camera,
                m_map.GroundCollider,
                Entities,
                Controller,
                m_world.Predictor.Position,
                out WorldPosition _,
                out EntityId picked,
                isWalking);
            Picked = picked;
            m_tick++;
            m_driver.Tick(m_tick);
            return result;
        }

        public void Dispose()
        {
            m_pointer.Dispose();
            m_combat?.Dispose();
            m_windows?.Dispose();
            m_skills?.Dispose();
        }

        private static NavigationGrid CreateGrid()
        {
            int rows = Yard.Length;
            int columns = Yard[0].Length;
            var cells = new NavigationCell[rows * columns];
            for (int row = 0; row < rows; row++)
            {
                string text = Yard[rows - 1 - row];
                for (int column = 0; column < columns; column++)
                {
                    NavigationSurface surface = text[column] == '.' ? NavigationSurface.Floor : NavigationSurface.Wall;
                    cells[row * columns + column] = NavigationCell.Level(surface, 0f);
                }
            }

            return new NavigationGrid(columns, rows, 1f, 0f, 0f, 0.3f, 0.4f, cells);
        }
    }
}
}
