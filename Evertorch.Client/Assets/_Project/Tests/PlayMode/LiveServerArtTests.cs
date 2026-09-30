using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 11 art pass's presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>ArtPassAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of what the real
///     client draws.
/// </summary>
public sealed class LiveServerArtTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string WearerName = "LiveArtOne";
    private const string WatcherName = "LiveArtTwo";
    private const string OtherName = "LiveArtThree";
    private const string TrainingSword = "item.weapon.training_sword";
    private const float StartTimeoutSeconds = 30f;
    private const int TestTimeoutMs = 300_000;

    private LiveDatabase? m_database;
    private LiveServer? m_server;
    private GameObject? m_client;
    private InputActionAsset? m_actions;
    private LiteNetLibClientTransport? m_otherSocket;

    [UnityTearDown]
    public IEnumerator StopEverything()
    {
        if (m_client != null)
        {
            Object.Destroy(m_client);
            m_client = null;
        }

        m_otherSocket?.Dispose();
        m_otherSocket = null;
        m_server?.Dispose();
        m_server = null;
        m_database?.Dispose();
        m_database = null;
        if (m_actions != null)
        {
            Object.Destroy(m_actions);
            m_actions = null;
        }

        // As in LiveServerCombatTests: only a scene the client loaded is unloaded, never the test runner's own.
        Scene loaded = SceneManager.GetActiveScene();
        Scene empty = SceneManager.CreateScene($"Empty {Guid.NewGuid():N}");
        SceneManager.SetActiveScene(empty);
        if (loaded.isLoaded
            && (MapSceneResolver.IsMapScene(loaded.name) || loaded.name == BootstrapRedirect.MainMenuScene))
        {
            yield return SceneManager.UnloadSceneAsync(loaded);
        }
    }

    // A character stored wearing the training sword enters the training ground: the server's inventory names the sword
    // worn, and the client draws the Adventurer's body from its key (Prototype Content §2, §2.1).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Body_OfAnAdventurerWearingTheSword_IsDrawnFromItsKey()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        GameClient client = CreateClient(actionsPath);

        yield return EnterByName(client, WearerName, () => database.SeedWornWeapon(WearerName, TrainingSword));
        ClientWorld world = client.World!;
        yield return WaitUntil(() => LocalBody(client)?.HasBody == true, StartTimeoutSeconds);

        Assert.That(
            world.Inventory.Rows.Single(row => row.Item.Value == TrainingSword).Slot,
            Is.EqualTo(EquipmentSlot.Weapon),
            $"the sword worn: {server.JoinOutput()}");
        EntityView body = LocalBody(client)!;
        Assert.That((body.Key, body.IsPlaceholder), Is.EqualTo(("character_adventurer", false)), client.Status);
        Assert.That(body.HasClips, Is.True, "the delivered Adventurer plays clips");
        yield return WaitUntil(() => HeldBy(body) != null, StartTimeoutSeconds);
        Assert.That(HeldBy(body), Is.Not.Null, "the sword from its own inventory, in its hand");
        Assert.That(body.AttackClip, Is.EqualTo("attack_sword"), "a sword's grip swings the sword clip");
        yield return WaitUntil(() => body.BodyAnimator!.CurrentClip == "idle", 2f);
        Assert.That(body.BodyAnimator!.CurrentClip, Is.EqualTo("idle"), "standing");

        // A walk plays the run, and the stop turns back to the idle (Gameplay Systems §8).
        WorldPosition from = world.Predictor.Position;
        Assert.That(
            new[] { (4f, 0f), (-4f, 0f), (0f, 4f), (0f, -4f) }.Any(offset => client.Controller!.TryMoveTo(
                from,
                new WorldPosition(from.X + offset.Item1, from.Y, from.Z + offset.Item2))),
            Is.True,
            "a walkable spot 4 m away");
        yield return WaitUntil(() => body.BodyAnimator!.CurrentClip == "run", StartTimeoutSeconds);
        Assert.That(body.BodyAnimator!.CurrentClip, Is.EqualTo("run"), "walking");
        yield return WaitUntil(() => body.BodyAnimator!.CurrentClip == "idle", StartTimeoutSeconds);
        Assert.That(body.BodyAnimator!.CurrentClip, Is.EqualTo("idle"), "stopped");
    }

    // Another player, wearing the training sword, is drawn with it in its hand; a click on the ground 0.9 m to the side
    // of it walks there rather than selecting it, since no pick sphere is wider than 0.7 m, where the delivered 1.1 m
    // would take it (Prototype Content §2, §2.1, §4; Network Protocol §9).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator OtherPlayer_WearingTheSword_HoldsItInHand_AndAGroundClickBesideItWalks()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, server.JoinOutput());
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, WatcherName);
        ClientWorld world = client.World!;
        ClientContent content = client.Content!;

        // The other player: the client's own networking without Unity's views, stored wearing the sword before it
        // enters.
        m_otherSocket = new LiteNetLibClientTransport("evertorch", 5000);
        var other = new ClientConnection(
            m_otherSocket,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, content.Version, "dev:liveart"),
            content);
        bool hasList = false;
        other.CharactersChanged += () => hasList = true;
        other.Connect("127.0.0.1", port);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return hasList && other.State == ClientConnectionState.SelectingCharacter;
            },
            StartTimeoutSeconds);
        other.CreateCharacter(OtherName);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.Characters.Any(entry => entry.Name == OtherName);
            },
            StartTimeoutSeconds);
        database.SeedWornWeapon(OtherName, TrainingSword);
        other.EnterWorld(other.Characters.Single(entry => entry.Name == OtherName).Character);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.World?.Inventory.IsCurrent == true;
            },
            StartTimeoutSeconds);
        Assert.That(other.World, Is.Not.Null, $"{other.LocalError} {server.JoinOutput()}");
        EntityId seen = other.World!.LocalEntity;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.RemoteViews.TryGetValue(seen, out EntityView? view) && HeldBy(view) != null;
            },
            StartTimeoutSeconds);

        EntityView body = client.RemoteViews[seen];
        Assert.That(world.Remotes[seen].WornWeapon, Is.EqualTo(TrainingSword), "the spawn names the sword");
        Assert.That(HeldBy(body), Is.Not.Null, "drawn in its hand");
        Assert.That(body.AttackClip, Is.EqualTo("attack_sword"));

        // Both entered on the same spawn point: walk apart first, so the watcher's own body is not under the click.
        WorldPosition from = world.Predictor.Position;
        Assert.That(
            new[] { (3f, 0f), (-3f, 0f), (0f, 3f), (0f, -3f) }.Any(offset => client.Controller!.TryMoveTo(
                from,
                new WorldPosition(from.X + offset.Item1, from.Y, from.Z + offset.Item2))),
            Is.True,
            "a place 3 m away");
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return !client.Controller!.HasPath && world.Predictor.PendingCount == 0;
            },
            StartTimeoutSeconds);

        Camera camera = Camera.main!;
        Vector3 side = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized;
        Vector3 beside = body.transform.position + side * 0.9f;
        InputSystem.QueueStateEvent(
            mouse,
            new MouseState { position = camera.WorldToScreenPoint(beside) }.WithButton(MouseButton.Left));
        InputSystem.Update();
        InputSystem.QueueStateEvent(mouse, new MouseState { position = camera.WorldToScreenPoint(beside) });
        InputSystem.Update();
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.Controller!.HasPath || world.Target != default;
            },
            2f);

        Assert.That(world.Target, Is.EqualTo(default(EntityId)), "the click did not select the player beside it");
        Assert.That(client.Controller!.HasPath, Is.True, "it walks to the ground clicked");
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the other player's traffic");
    }

    private static GameObject? HeldBy(EntityView body)
    {
        Transform? held = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(part => part.name == "Held");
        return held != null ? held.gameObject : null;
    }

    private static EntityView? LocalBody(GameClient client)
    {
        return (EntityView?)typeof(GameClient)
            .GetField("m_localView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client);
    }

    private static string RequirePrerequisites()
    {
        if (!LiveServer.IsBuilt())
        {
            Assert.Inconclusive(LiveServer.MissingPrerequisites);
        }

        string? mismatch = LiveServer.ContentMismatch();
        if (mismatch != null)
        {
            Assert.Fail(mismatch);
        }

        string actionsPath = Path.Combine(Application.dataPath, ActionsAsset);
        Assert.That(File.Exists(actionsPath), Is.True, actionsPath);
        return actionsPath;
    }

    // Creates the named character, runs beforeEntering, and enters the world with it.
    private static IEnumerator EnterByName(GameClient client, string name, Action? beforeEntering = null)
    {
        yield return LiveSignIn.PressConnect(client);
        yield return WaitUntil(() => client.Connection != null, StartTimeoutSeconds);
        ClientConnection connection = client.Connection!;
        bool hasList = false;
        connection.CharactersChanged += () => hasList = true;
        yield return WaitUntil(() => hasList, StartTimeoutSeconds);
        Assert.That(hasList, Is.True, $"no character list: {client.Status}");
        client.CreateCharacter(name);
        yield return WaitUntil(() => connection.Characters.Any(entry => entry.Name == name), StartTimeoutSeconds);
        beforeEntering?.Invoke();
        client.EnterWorld(connection.Characters.Single(entry => entry.Name == name).Character);
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

    private GameClient CreateClient(string actionsPath)
    {
        m_actions = InputActionAsset.FromJson(File.ReadAllText(actionsPath));
        m_client = new GameObject("TestGameClient");
        m_client.SetActive(false);
        GameClient client = m_client.AddComponent<GameClient>();

        // The bootstrap scene assigns the actions in the inspector; this test builds the client itself.
        typeof(GameClient)
            .GetField("m_inputActions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, m_actions);
        LiveSignIn.Prepare(client, m_server!);
        m_client.SetActive(true);
        return client;
    }
}
}
