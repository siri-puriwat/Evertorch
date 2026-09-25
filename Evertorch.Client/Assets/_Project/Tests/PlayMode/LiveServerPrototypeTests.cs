using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 6 prototype acceptance's presentation checks (Prototype Content §9; Coding Standards §10): the
///     real <see cref="GameClient" /> and a second, bare <see cref="ClientConnection" /> against the real server
///     process and a real database. The headless <c>PrototypeAcceptanceTests</c> hold every step; here each later
///     line adds only a bounded check of what the real client draws.
/// </summary>
public sealed class LiveServerPrototypeTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string ClientName = "LiveProtoOne";
    private const string OtherName = "LiveProtoTwo";
    private const float StartTimeoutSeconds = 30f;
    private const float StepTimeoutSeconds = 15f;
    private const float DrawnDistance = 1e-3f;
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

    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator TwoPlayers_TheRealClientDrawsTheOtherWhereTheServerHasIt()
    {
        string actionsPath = RequirePrerequisites(out ClientContent content);
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, $"server output: {server.JoinOutput()}");

        GameClient client = CreateClient(port, actionsPath);
        yield return EnterByName(client, ClientName);
        ClientWorld world = client.World!;

        // The second player: the client's own networking and gameplay code without Unity's views, as a second window
        // would run it.
        m_otherSocket = new LiteNetLibClientTransport("evertorch", 5000);
        var otherLink = new LossyTransport(m_otherSocket, 7, () => Time.realtimeSinceStartupAsDouble);
        var other = new ClientConnection(
            otherLink,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, content.Version, "dev:liveprototype"),
            content);
        var picker = new CharacterPicker(OtherName);
        other.Connect("127.0.0.1", port);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                picker.Poll(other);
                return other.World?.Inventory.IsCurrent == true;
            },
            StepTimeoutSeconds);
        Assert.That(other.World, Is.Not.Null, $"{other.LocalError} {other.DisconnectCause} {server.JoinOutput()}");
        ClientWorld otherWorld = other.World!;
        EntityId otherEntity = otherWorld.LocalEntity;
        long otherCharacter = other.Characters.Single(entry => entry.Name == OtherName).Character.Value;

        var controller = new MovementController(otherWorld.Grid);
        var driver = new LocalPlayerDriver(controller, new MoveIntentProducer(), otherWorld, other);
        var clock = new FixedTickClock(1f / other.ServerTickRate);
        yield return Simulate(other, driver, otherWorld, clock, StepTimeoutSeconds, () => Drawn(client, otherEntity));
        Assert.That(Drawn(client, otherEntity), Is.True, "the real client draws the second player");

        controller.SetManualDirection(1f, 0f);
        yield return Simulate(other, driver, otherWorld, clock, 1f, () => false);
        controller.SetManualDirection(0f, 0f);
        yield return Simulate(
            other,
            driver,
            otherWorld,
            clock,
            StepTimeoutSeconds,
            () => otherWorld.Predictor.PendingCount == 0);

        // The console republishes what it reads once a second, so wait out one full period of quiet.
        yield return Simulate(other, driver, otherWorld, clock, 1.5f, () => false);
        server.ClearOutput();
        server.SendCommand("players");
        yield return WaitUntil(() => server.HasOutput($"character {otherCharacter} "), StepTimeoutSeconds);
        Assert.That(
            server.TryReadPlayerPosition(otherCharacter, out float serverX, out float serverZ),
            Is.True,
            server.JoinOutput());
        Assert.That(serverX, Is.GreaterThan(2f), "the second player really walked");

        Vector3 drawn = client.RemoteViews[otherEntity].transform.position;
        Assert.That(drawn.x, Is.EqualTo(serverX).Within(DrawnDistance), $"server output: {server.JoinOutput()}");
        Assert.That(drawn.z, Is.EqualTo(serverZ).Within(DrawnDistance));
        Assert.That(world.Remotes[otherEntity].Kind, Is.EqualTo(EntityKind.Player));
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the second player's traffic");
    }

    private static bool Drawn(GameClient client, EntityId entity)
    {
        return client.RemoteViews.TryGetValue(entity, out EntityView? view) && view != null;
    }

    private static string RequirePrerequisites(out ClientContent content)
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

        ClientContent? loaded = LiveServer.LoadClientContent(out string contentError);
        Assert.That(loaded, Is.Not.Null, contentError);
        content = loaded!;
        string actionsPath = Path.Combine(Application.dataPath, ActionsAsset);
        Assert.That(File.Exists(actionsPath), Is.True, actionsPath);
        return actionsPath;
    }

    private static IEnumerator EnterByName(GameClient client, string name)
    {
        yield return WaitUntil(() => client.Connection != null, StartTimeoutSeconds);
        ClientConnection connection = client.Connection!;
        bool hasList = false;
        connection.CharactersChanged += () => hasList = true;
        yield return WaitUntil(() => hasList, StartTimeoutSeconds);
        Assert.That(hasList, Is.True, $"no character list: {client.Status}");
        client.CreateCharacter(name);
        yield return WaitUntil(() => connection.Characters.Any(entry => entry.Name == name), StartTimeoutSeconds);
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

    private static IEnumerator Simulate(
        ClientConnection connection,
        LocalPlayerDriver driver,
        ClientWorld world,
        FixedTickClock clock,
        float seconds,
        Func<bool> isDone)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline && !isDone())
        {
            connection.Poll();
            int due = clock.Advance(Time.unscaledDeltaTime);
            for (int index = 0; index < due; index++)
            {
                driver.Tick(clock.NextTick());
            }

            world.Advance(Time.unscaledDeltaTime);
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
}
}
