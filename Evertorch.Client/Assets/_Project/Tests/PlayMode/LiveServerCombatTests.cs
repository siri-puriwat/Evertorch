using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The whole client against the real server process and a real database (Milestone 3 verification "the player can
///     kill the training slime and see a slime-gel drop"; Milestone 4 verification "a pickup is committed before
///     success and survives a server restart"; the Milestone 5 acceptance path, Coding Standards §10):
///     <see cref="GameClient" /> with the project's input actions, driven only by simulated devices: WASD and a ground
///     click to walk, Tab to target, the gamepad's West button to attack, R to respawn, F to pick up, and the login
///     panel to reconnect.
/// </summary>
public sealed class LiveServerCombatTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string SlimeGel = "item.material.slime_gel";
    private const string GelModel = "pickup_slime_gel";
    private const float StartTimeoutSeconds = 30f;
    private const float FightTimeoutSeconds = 180f;
    private const float WalkTimeoutSeconds = 15f;
    private const float ConvergeTimeoutSeconds = 15f;
    private const float ConvergedDistance = 1e-3f;

    // Past every budget inside the test, the runner's 180 s default among them, which the fight alone can reach.
    private const int FightTestTimeoutMs = 300_000;
    private const int WholePathTimeoutMs = 600_000;

    private readonly List<ItemDropped> m_dropped = new();
    private int m_kills;
    private int m_respawns;
    private bool m_hasCheckedTargetFrame;
    private LiveDatabase? m_database;
    private LiveServer? m_server;
    private GameObject? m_client;
    private InputActionAsset? m_actions;

    [UnityTearDown]
    public IEnumerator StopEverything()
    {
        if (m_client != null)
        {
            Object.Destroy(m_client);
            m_client = null;
        }

        m_server?.Dispose();
        m_server = null;
        m_database?.Dispose();
        m_database = null;
        if (m_actions != null)
        {
            Object.Destroy(m_actions);
            m_actions = null;
        }

        m_dropped.Clear();
        m_kills = 0;
        m_respawns = 0;
        m_hasCheckedTargetFrame = false;

        // Leave the next test an empty scene rather than the map, whose camera and ground would still be there. Only a
        // scene the client loads is unloaded: a test that fails before its map loads leaves the test runner's own scene
        // active, which play mode numbers among the build scenes, and unloading it would silently end the whole run.
        Scene loaded = SceneManager.GetActiveScene();
        Scene empty = SceneManager.CreateScene($"Empty {Guid.NewGuid():N}");
        SceneManager.SetActiveScene(empty);
        if (loaded.isLoaded
            && (MapSceneResolver.IsMapScene(loaded.name) || loaded.name == BootstrapRedirect.MainMenuScene))
        {
            yield return SceneManager.UnloadSceneAsync(loaded);
        }
    }

    [UnityTest]
    [Timeout(FightTestTimeoutMs)]
    public IEnumerator Player_FightsSlimesWithKeysAndButtons_UntilOneDropsGelThatIsDrawnFromItsModel()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        Assert.That(m_server!.TryReadListeningPort(out int port), Is.True, $"server output: {m_server.JoinOutput()}");

        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        GameClient client = CreateClient(port, actionsPath);
        yield return CreateAndEnterThroughTheLoginPanel(client, "LiveFighter");
        yield return WaitUntil(() => client.World != null && client.Combat != null, StartTimeoutSeconds);
        Assert.That(client.World, Is.Not.Null, $"{client.Status} server output: {m_server.JoinOutput()}");
        ClientWorld world = client.World!;
        yield return WaitUntil(() => world.Inventory.IsCurrent, StartTimeoutSeconds);
        Assert.That(world.Inventory.IsCurrent, Is.True, "the baseline ends with the inventory snapshot");
        Assert.That(world.Inventory.Rows, Is.Empty, "a new character carries nothing");

        yield return FightUntilGelDrops(client, world, keyboard, gamepad);

        EntityView? gel = FindGelView(client);
        Assert.That(
            gel,
            Is.Not.Null,
            $"kills {m_kills}, respawns {m_respawns}, drops {m_dropped.Count}, status {client.Status}");
        Assert.That(gel!.HasBody, Is.True);
        Assert.That(gel.IsPlaceholder, Is.False, "the body is the pickup_slime_gel prefab, not the placeholder");
        Assert.That(m_dropped.Any(dropped => dropped.ItemId == SlimeGel), Is.True);
        Assert.That(m_kills, Is.GreaterThanOrEqualTo(1));
        Debug.Log($"Live combat: {m_kills} kills, {m_respawns} respawns, {m_dropped.Count} drops announced");
    }

    [UnityTest]
    [Timeout(WholePathTimeoutMs)]
    public IEnumerator Player_WalksFightsAndPicksUp_ThenKeepsTheGelAcrossAReconnectAndAServerRestart()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        Assert.That(m_server!.TryReadListeningPort(out int port), Is.True, $"server output: {m_server.JoinOutput()}");

        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        GameClient client = CreateClient(port, actionsPath);
        yield return CreateAndEnterThroughTheLoginPanel(client, "LivePicker");
        yield return WaitUntil(() => client.World?.Inventory.IsCurrent == true, StartTimeoutSeconds);
        Assert.That(client.World, Is.Not.Null, $"{client.Status} server output: {m_server.JoinOutput()}");
        ClientWorld world = client.World!;
        yield return WalkOnABadLink(client, world, keyboard, mouse);
        yield return FightUntilGelDrops(client, world, keyboard, gamepad);
        Assert.That(FindGelView(client), Is.Not.Null, $"kills {m_kills}, respawns {m_respawns}");

        var pickedUp = new List<ItemPickedUp>();
        world.ItemPickedUpReceived += pickedUp.Add;
        yield return WaitUntil(() => !world.IsLocalDead, 5f);
        yield return Tap(keyboard.fKey);
        yield return WaitUntil(() => HeldGel(world) > 0, StartTimeoutSeconds);
        uint held = HeldGel(world);
        Assert.That(held, Is.GreaterThan(0u), $"last refusal {world.LastRejection}, status {client.Status}");
        Assert.That(pickedUp.Any(picked => picked.Recipient == world.LocalEntity), Is.True);
        uint revision = world.Inventory.Revision;
        FeedbackLines feedback = client.GetComponentsInChildren<FeedbackLines>(true).Single();
        InventoryWindow inventory = client.GetComponentsInChildren<InventoryWindow>(true).Single();
        yield return WaitUntil(() => inventory.Text == $"Slime Gel x {held}", 5f);
        Assert.That(feedback.Text, Does.Contain("Picked up Slime Gel x "), "the pickup is announced by name");
        Assert.That(inventory.Text, Is.EqualTo($"Slime Gel x {held}"), "the window lists the gel by name");

        client.Disconnect();
        yield return WaitUntil(() => client.World == null, StartTimeoutSeconds);
        yield return ReconnectThroughTheLoginPanel(client, port);
        Assert.That(HeldGel(client.World!), Is.EqualTo(held), "the retained character still holds the gel");
        Assert.That(client.World!.Inventory.Revision, Is.EqualTo(revision));

        // Killed, not stopped: nothing is saved on the way out, so only what was committed can come back.
        client.Disconnect();
        yield return WaitUntil(() => client.World == null, StartTimeoutSeconds);
        m_server.Dispose();
        m_server = new LiveServer();
        m_server.Start(m_database!, "--World:RandomSeed=11");
        LiveServer restarted = m_server;
        yield return WaitUntil(() => restarted.TryReadListeningPort(out int _), StartTimeoutSeconds);
        Assert.That(restarted.TryReadListeningPort(out int restartedPort), Is.True, restarted.JoinOutput());
        yield return ReconnectThroughTheLoginPanel(client, restartedPort);
        Assert.That(HeldGel(client.World!), Is.EqualTo(held), "the gel was loaded from the database");
        Assert.That(client.World!.Inventory.Revision, Is.EqualTo(revision));
        Debug.Log($"Live pickup: {held} gel kept across a reconnect and a server restart, revision {revision}");
    }

    private static string RequirePrerequisites()
    {
        string actionsPath = Path.Combine(Application.dataPath, ActionsAsset);
        if (!LiveServer.IsBuilt() || !File.Exists(actionsPath))
        {
            Assert.Inconclusive(LiveServer.MissingPrerequisites);
        }

        // A stale client package is a setup to fix, not a missing prerequisite.
        string? mismatch = LiveServer.ContentMismatch();
        if (mismatch != null)
        {
            Assert.Fail(mismatch);
        }

        return actionsPath;
    }

    private static uint HeldGel(ClientWorld world)
    {
        if (!world.Inventory.IsCurrent)
        {
            return 0u;
        }

        uint held = 0u;
        foreach (InventoryEntry row in world.Inventory.Rows)
        {
            if (row.Item.Value == SlimeGel)
            {
                held += row.Quantity;
            }
        }

        return held;
    }

    private static EntityView? FindGelView(GameClient client)
    {
        ClientWorld? world = client.World;
        if (world == null)
        {
            return null;
        }

        foreach (KeyValuePair<EntityId, EntityView> pair in client.RemoteViews)
        {
            if (world.Remotes.TryGetValue(pair.Key, out RemoteEntity? remote)
                && remote.Kind == EntityKind.ItemDrop
                && remote.DefinitionId == SlimeGel
                && pair.Value.Key == GelModel
                && pair.Value.HasBody)
            {
                return pair.Value;
            }
        }

        return null;
    }

    // The login panel's name field and buttons, as a player uses them to pick a character; the status bar then names
    // the character. A refused entry is never answered (Network Protocol §4), so the list has to stay on screen for
    // another choice.
    private static IEnumerator CreateAndEnterThroughTheLoginPanel(GameClient client, string name)
    {
        yield return WaitUntil(() => client.Connection != null, StartTimeoutSeconds);
        ClientConnection connection = client.Connection!;
        bool hasList = false;
        connection.CharactersChanged += () => hasList = true;
        yield return WaitUntil(() => hasList, StartTimeoutSeconds);
        Assert.That(hasList, Is.True, $"no character list: {client.Status}");

        TMP_InputField field = client.GetComponentsInChildren<TMP_InputField>(true)
            .Single(candidate => candidate.transform.parent.name == "New name");
        field.text = name;
        Button create = client.GetComponentsInChildren<Button>(true)
            .Single(button => button.name == "Create character");
        Assert.That(create.gameObject.activeInHierarchy, Is.True, "the login panel shows character creation");
        create.onClick.Invoke();
        yield return WaitUntil(() => connection.Characters.Any(entry => entry.Name == name), StartTimeoutSeconds);
        Assert.That(connection.LastCreateOutcome, Is.EqualTo(CreateCharacterOutcome.Created), client.Status);

        // The login panel refreshes its buttons in its own Update.
        yield return null;
        Button enter = client.GetComponentsInChildren<Button>(true).Single(button => button.name == "Enter 1");
        Assert.That(enter.gameObject.activeInHierarchy, Is.True, "the new character can be entered");
        client.EnterWorld(new CharacterId(long.MaxValue));
        yield return null;
        Assert.That(connection.State, Is.EqualTo(ClientConnectionState.EnteringWorld));
        Assert.That(enter.gameObject.activeInHierarchy, Is.True, "an unanswered entry keeps the list on screen");
        enter.onClick.Invoke();

        TMP_Text shownName = client.GetComponentsInChildren<StatusBar>(true).Single()
            .GetComponentsInChildren<TMP_Text>(true)
            .Single(label => label.name == "Name");
        yield return WaitUntil(() => shownName.text.StartsWith(name, StringComparison.Ordinal), StartTimeoutSeconds);
        Assert.That(shownName.text, Does.StartWith(name), "the status bar names the character in the world");
    }

    // After a close the client leaves the map for the main menu and says why in its own words. Nothing reconnects by
    // itself: the login panel's Reconnect, pressed on the port the player typed, connects and enters the last
    // character.
    private static IEnumerator ReconnectThroughTheLoginPanel(GameClient client, int port)
    {
        yield return WaitUntil(
            () => SceneManager.GetActiveScene().name == BootstrapRedirect.MainMenuScene,
            StartTimeoutSeconds);
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(BootstrapRedirect.MainMenuScene), "no empty map");
        Assert.That(client.Status, Is.EqualTo(DisconnectMessages.ForCause(TransportDisconnectCause.ClosedLocally)));
        yield return new WaitForSecondsRealtime(1f);
        Assert.That(client.Connection!.State, Is.EqualTo(ClientConnectionState.Disconnected), "no reconnect by itself");

        TMP_InputField portField = client.GetComponentsInChildren<TMP_InputField>(true)
            .Single(candidate => candidate.transform.parent.name == "Port");
        portField.text = port.ToString(CultureInfo.InvariantCulture);
        Button reconnect = client.GetComponentsInChildren<Button>(true)
            .Single(button => button.name == "Reconnect");
        Assert.That(reconnect.gameObject.activeInHierarchy, Is.True, "the login panel offers Reconnect");
        reconnect.onClick.Invoke();
        yield return WaitUntil(() => client.World?.Inventory.IsCurrent == true, StartTimeoutSeconds);
        Assert.That(client.World?.Inventory.IsCurrent, Is.True, client.Status);
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private IEnumerator StartDatabaseAndServer()
    {
        Task<LiveDatabase> starting = LiveDatabase.StartAsync();
        yield return new WaitUntil(() => starting.IsCompleted);
        Assert.That(starting.IsFaulted, Is.False, starting.Exception?.GetBaseException().Message);
        m_database = starting.Result;

        LiveServer server = m_server = new LiveServer();
        server.Start(m_database, "--World:RandomSeed=11");
        Assert.That(server.IsTiedToEditor || !KillOnCloseJob.IsSupported, Is.True, "the server ends with the editor");
        yield return WaitUntil(() => server.TryReadListeningPort(out int _), StartTimeoutSeconds);
    }

    // WASD and a ground click as a player makes them, on a simulated bad link. A stop the server never hears leaves
    // it walking on the last input for as long as it holds one, so the client must take the server's word: a
    // correction, smoothed rather than snapped. After each walk the client ends where the server process says it is.
    private IEnumerator WalkOnABadLink(GameClient client, ClientWorld world, Keyboard keyboard, Mouse mouse)
    {
        LossyTransport link = client.Link!;
        MovementController controller = client.Controller!;
        WorldPosition start = world.Predictor.Position;

        link.LatencyMilliseconds = 50;
        HoldKeys(keyboard, Key.W);
        yield return new WaitForSecondsRealtime(1f);
        float before = world.Smoother.LargestCorrection;
        link.LossPercent = 100;
        HoldKeys(keyboard);
        yield return new WaitForSecondsRealtime(0.5f);
        link.LossPercent = 0;
        yield return AwaitConvergence(world, "a lost stop");
        Assert.That(
            world.Smoother.LargestCorrection,
            Is.GreaterThan(Math.Max(before, 0.5f)),
            "the server corrected the prediction");
        Assert.That(world.Smoother.Snaps, Is.Zero, "the correction was smoothed, not snapped");

        int dropped = link.Dropped;
        link.JitterMilliseconds = 10;
        link.LossPercent = 10;
        link.ReorderPercent = 5;
        HoldKeys(keyboard, Key.S);
        yield return new WaitForSecondsRealtime(1.5f);
        HoldKeys(keyboard);
        yield return AwaitConvergence(world, "a lossy WASD walk");

        // Back towards the spawn, where the slimes are.
        WorldPosition target = ClearGroundNear(client, world, start);
        Vector3 onScreen = Camera.main!.WorldToScreenPoint(new Vector3(target.X, target.Y, target.Z));
        ClickAt(mouse, onScreen);
        yield return WaitUntil(() => controller.HasPath, 2f);
        Assert.That(controller.HasPath, Is.True, $"a click at {onScreen} on {target} started a walk");
        yield return WaitUntil(() => !controller.HasPath, WalkTimeoutSeconds);
        yield return AwaitConvergence(world, "a lossy click walk");
        Assert.That(link.Dropped, Is.GreaterThan(dropped), "the link really lost messages");
        Assert.That(world.Smoother.Snaps, Is.Zero, "no correction was large enough to snap");
        Debug.Log(
            $"Live walk: largest correction {world.Smoother.LargestCorrection} m, dropped {link.Dropped}, "
            + $"reordered {link.Reordered}");

        link.LatencyMilliseconds = 0;
        link.JitterMilliseconds = 0;
        link.LossPercent = 0;
        link.ReorderPercent = 0;
    }

    // The console republishes what it reads once a second, so agreement shows up within a few of those.
    private IEnumerator AwaitConvergence(ClientWorld world, string step)
    {
        LiveServer server = m_server!;
        float deadline = Time.realtimeSinceStartup + ConvergeTimeoutSeconds;
        bool isConverged = false;
        string serverView = string.Empty;
        while (!isConverged && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (world.Predictor.PendingCount != 0)
            {
                continue;
            }

            server.ClearOutput();
            server.SendCommand("players");
            yield return WaitUntil(() => server.HasOutput(" at ("), 5f);
            serverView = server.JoinOutput();
            WorldPosition predicted = world.Predictor.Position;
            isConverged = server.TryReadPlayerPosition(out float x, out float z)
                && Mathf.Abs(predicted.X - x) <= ConvergedDistance
                && Mathf.Abs(predicted.Z - z) <= ConvergedDistance;
        }

        Assert.That(
            isConverged,
            Is.True,
            $"{step}: the client at {world.Predictor.Position} converges on the server: {serverView}");
    }

    // A reachable point two to six metres away with nothing drawn near it, so the click walks rather than attacks or
    // picks up. The preferred point first, then the compass around the player.
    private static WorldPosition ClearGroundNear(GameClient client, ClientWorld world, WorldPosition preferred)
    {
        WorldPosition from = world.Predictor.Position;
        var candidates = new List<WorldPosition> { preferred };
        for (int step = 0; step < 8; step++)
        {
            float angle = step * Mathf.PI / 4f;
            candidates.Add(new WorldPosition(from.X + 3f * Mathf.Cos(angle), from.Y, from.Z + 3f * Mathf.Sin(angle)));
        }

        var paths = new MovementController(world.Grid);
        foreach (WorldPosition point in candidates)
        {
            float distance = HorizontalDistance(from, point);
            bool isClear = client.RemoteViews.Values.All(view =>
                HorizontalDistance(new WorldPosition(view.transform.position.x, 0f, view.transform.position.z), point)
                >= 2f);
            if (distance >= 2f && distance <= 6f && isClear && paths.TryMoveTo(from, point))
            {
                return point;
            }
        }

        Assert.Fail($"no clear, reachable ground near {from} to click on");
        return from;
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float x = a.X - b.X;
        float z = a.Z - b.Z;
        return Mathf.Sqrt(x * x + z * z);
    }

    // A full keyboard state lasts across frames, which the fixture's Press does not promise once a frame has run.
    private static void HoldKeys(Keyboard keyboard, params Key[] held)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(held));
        InputSystem.Update();
    }

    private static void ClickAt(Mouse mouse, Vector2 screenPosition)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPosition }.WithButton(MouseButton.Left));
        InputSystem.Update();
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPosition });
        InputSystem.Update();
    }

    private IEnumerator FightUntilGelDrops(GameClient client, ClientWorld world, Keyboard keyboard, Gamepad gamepad)
    {
        world.ItemDroppedReceived += m_dropped.Add;
        world.EntityDiedReceived += died =>
        {
            if (died.Entity != world.LocalEntity)
            {
                m_kills++;
            }
        };
        EntityId attacking = default;
        float deadline = Time.realtimeSinceStartup + FightTimeoutSeconds;
        while (Time.realtimeSinceStartup < deadline && FindGelView(client) == null)
        {
            if (world.IsLocalDead)
            {
                yield return Tap(keyboard.rKey);
                m_respawns++;
                yield return WaitUntil(() => !world.IsLocalDead, 5f);
                attacking = default;
            }
            else if (world.Target == default)
            {
                yield return Tap(keyboard.tabKey);
                yield return WaitUntil(() => world.Target != default, 2f);
                if (world.Target != default && !m_hasCheckedTargetFrame)
                {
                    m_hasCheckedTargetFrame = true;
                    yield return ExpectTargetFrame(client);
                }
            }
            else if (world.Target != attacking)
            {
                attacking = world.Target;
                yield return Tap(gamepad.buttonWest);
            }
            else
            {
                yield return null;
            }
        }
    }

    // The frame follows the confirmed target: the slime by its display name, and how far away it is.
    private static IEnumerator ExpectTargetFrame(GameClient client)
    {
        TargetFrame frame = client.GetComponentsInChildren<TargetFrame>(true).Single();
        TMP_Text name = frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Name");
        TMP_Text detail = frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Detail");
        yield return WaitUntil(() => frame.IsVisible && detail.text.EndsWith(" m", StringComparison.Ordinal), 2f);
        Assert.That(frame.IsVisible, Is.True, "the frame shows the confirmed target");
        Assert.That(name.text, Is.EqualTo("Training Slime"));
        Assert.That(detail.text, Does.EndWith(" m"), "the frame gives the distance to the target");
    }

    private GameClient CreateClient(int port, string actionsPath)
    {
        m_actions = InputActionAsset.FromJson(File.ReadAllText(actionsPath));
        m_client = new GameObject("TestGameClient");
        m_client.SetActive(false);
        GameClient client = m_client.AddComponent<GameClient>();

        // The bootstrap scene assigns the actions in the inspector; this test builds the client itself.
        typeof(GameClient)
            .GetField("m_inputActions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, m_actions);
        client.Host = "127.0.0.1";
        client.Port = port;
        m_client.SetActive(true);
        return client;
    }

    private IEnumerator Tap(ButtonControl button)
    {
        Press(button);
        yield return null;
        Release(button);
        yield return null;
    }
}
}
