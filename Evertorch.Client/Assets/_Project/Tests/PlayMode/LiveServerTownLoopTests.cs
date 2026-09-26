using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 7 town loop's presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>TownLoopAcceptanceTests</c> hold every step; here each later line adds only a bounded check of what the real
///     client draws.
/// </summary>
public sealed class LiveServerTownLoopTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string ClientName = "LiveTownOne";
    private const string GroundScene = "10_TrainingGround";
    private const string LocalViewName = "LocalPlayer";
    private const float StartTimeoutSeconds = 30f;
    private const float StepTimeoutSeconds = 15f;
    private const float DrawnDistance = 1e-3f;
    private const int TestTimeoutMs = 300_000;

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

    // The real client leaves town through the ground's portal and comes back through the field's (Gameplay Systems
    // §4.2, §5.1): the login panel never shows through either change, only the ground is drawn again, and after a
    // walk in town the ground's scene draws the player where the server has the character.
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Crossing_ToTheFieldAndBack_TheGroundDrawsThePlayerWhereTheServerHasIt()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, $"server output: {server.JoinOutput()}");
        GameClient client = CreateClient(port, actionsPath);
        yield return EnterByName(client, ClientName);
        ClientWorld town = client.World!;
        LoginPanel login = client.GetComponentInChildren<LoginPanel>();
        bool wasLoginShown = false;

        Assert.That(
            client.Controller!.TryMoveTo(town.Predictor.Position, new WorldPosition(22.2f, 0f, 0f)),
            Is.True,
            "a way into the ground's portal");
        yield return WaitUntil(
            () =>
            {
                wasLoginShown |= login.IsVisible;
                return client.World != null && client.World != town && client.World.Inventory.IsCurrent;
            },
            StepTimeoutSeconds);
        ClientWorld field = client.World!;
        Assert.That(field.Map.Value, Is.EqualTo("map.training_field"), $"{client.Status} {server.JoinOutput()}");

        // The server takes the character back only a second after it arrived, so it waits inside the portal.
        Assert.That(
            client.Controller!.TryMoveTo(field.Predictor.Position, new WorldPosition(-22.2f, 0f, 0f)),
            Is.True,
            "a way into the field's portal");
        yield return WaitUntil(
            () =>
            {
                wasLoginShown |= login.IsVisible;
                return client.World != null && client.World != field && client.World.Inventory.IsCurrent;
            },
            StepTimeoutSeconds);
        ClientWorld ground = client.World!;
        Assert.That(ground.Map.Value, Is.EqualTo("map.training_ground"), $"{client.Status} {server.JoinOutput()}");
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GroundScene));
        Assert.That(wasLoginShown, Is.False, "the login panel never showed through the changes");
        Assert.That(client.Status, Is.EqualTo("In Training Ground"));
        Assert.That(client.Connection!.MapEpoch, Is.EqualTo(2));
        Assert.That(
            Object.FindObjectsByType<GrayboxMap>(FindObjectsSortMode.None).Select(map => map.gameObject.scene.name),
            Is.EqualTo(new[] { GroundScene }),
            "only the ground is drawn");

        Assert.That(
            client.Controller!.TryMoveTo(ground.Predictor.Position, new WorldPosition(16f, 0f, -3f)),
            Is.True,
            "a way into town");
        yield return WaitUntil(
            () => client.Controller != null && !client.Controller.HasPath && ground.Predictor.PendingCount == 0,
            StepTimeoutSeconds);

        // The console republishes what it reads once a second, so wait out one full period of quiet; the drawn body's
        // last correction has faded long before that.
        yield return new WaitForSecondsRealtime(1.5f);
        long character = client.Connection.Characters.Single(entry => entry.Name == ClientName).Character.Value;
        server.ClearOutput();
        server.SendCommand("players");
        yield return WaitUntil(() => server.HasOutput(LiveServer.CharacterMarker(character)), StepTimeoutSeconds);
        Assert.That(server.TryReadPlayerPosition(character, out float serverX, out float serverZ), Is.True);
        string line = server.Output().First(text => text.Contains(LiveServer.CharacterMarker(character)));
        Assert.That(line, Does.Contain($"entity {ground.LocalEntity.Value} map.training_ground at"));
        Assert.That(serverX, Is.LessThan(19f), "the character walked into town from the arrival");

        var view = GameObject.Find(LocalViewName);
        Assert.That(view, Is.Not.Null, "the player is drawn");
        Assert.That(view.scene.name, Is.EqualTo(GroundScene), "in the ground's scene");
        Vector3 drawn = view.transform.position;
        Assert.That(drawn.x, Is.EqualTo(serverX).Within(DrawnDistance), server.JoinOutput());
        Assert.That(drawn.z, Is.EqualTo(serverZ).Within(DrawnDistance), server.JoinOutput());
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
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
