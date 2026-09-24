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
///     success and survives a server restart"): <see cref="GameClient" /> with the project's input actions, driven only
///     by simulated keys and buttons: Tab to target, the gamepad's West button to attack, R to respawn, F to pick up.
/// </summary>
public sealed class LiveServerCombatTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string SlimeGel = "item.material.slime_gel";
    private const string GelModel = "pickup_slime_gel";
    private const float StartTimeoutSeconds = 30f;
    private const float FightTimeoutSeconds = 180f;

    private readonly List<ItemDropped> m_dropped = new();
    private int m_kills;
    private int m_respawns;
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
    public IEnumerator Player_PicksUpTheGelWithTheKey_AndStillOwnsItAfterAReconnectAndAServerRestart()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        Assert.That(m_server!.TryReadListeningPort(out int port), Is.True, $"server output: {m_server.JoinOutput()}");

        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        GameClient client = CreateClient(port, actionsPath);
        yield return CreateAndEnterThroughTheLoginPanel(client, "LivePicker");
        yield return WaitUntil(() => client.World?.Inventory.IsCurrent == true, StartTimeoutSeconds);
        Assert.That(client.World, Is.Not.Null, $"{client.Status} server output: {m_server.JoinOutput()}");
        ClientWorld world = client.World!;
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
        yield return WaitUntil(() => server.TryReadListeningPort(out int _), StartTimeoutSeconds);
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
