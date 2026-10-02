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
///     The Milestone 13 "A dungeon and its boss" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>DungeonAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of what the real
///     client draws: the grotto's look, the variant bodies, the target frame, the slam's telegraph, and the boss's
///     words in the chat log.
/// </summary>
public sealed class LiveServerDungeonTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string ClientName = "LiveDungeonOne";
    private const string FieldScene = "11_TrainingField";
    private const float StartTimeoutSeconds = 30f;
    private const float StepTimeoutSeconds = 20f;
    private const int TestTimeoutMs = 300_000;

    // Where the field's way to the grotto begins, by its east wall.
    private static readonly WorldPosition FieldEastSide = new(20.5f, 0f, -8f);

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

        // As in LiveServerArtTests: only a scene the client loaded is unloaded, never the test runner's own.
        Scene loaded = SceneManager.GetActiveScene();
        Scene empty = SceneManager.CreateScene($"Empty {Guid.NewGuid():N}");
        SceneManager.SetActiveScene(empty);
        if (loaded.isLoaded
            && (MapSceneResolver.IsMapScene(loaded.name) || loaded.name == BootstrapRedirect.MainMenuScene))
        {
            yield return SceneManager.UnloadSceneAsync(loaded);
        }
    }

    // The real client crosses from the ground to the field and walks to the field's east side, where the way to the
    // grotto begins (Gameplay Systems §4.2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Crossing_TheRealClientWalksTheFieldTowardTheGrotto()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, ClientName);
        ClientWorld ground = client.World!;

        Assert.That(
            client.Controller!.TryMoveTo(ground.Predictor.Position, new WorldPosition(22.2f, 0f, 0f)),
            Is.True,
            "a way into the ground's portal");
        yield return WaitUntil(
            () => client.World != null && client.World != ground && client.World.Inventory.IsCurrent,
            StepTimeoutSeconds);
        ClientWorld field = client.World!;
        Assert.That(field.Map.Value, Is.EqualTo("map.training_field"), $"{client.Status} {server.JoinOutput()}");
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(FieldScene));

        Assert.That(
            client.Controller!.TryMoveTo(field.Predictor.Position, FieldEastSide),
            Is.True,
            "a way across the field");
        yield return WaitUntil(
            () => client.Controller != null && !client.Controller.HasPath && field.Predictor.PendingCount == 0,
            StepTimeoutSeconds);
        WorldPosition at = field.Predictor.Position;
        Assert.That(
            Math.Abs(at.X - FieldEastSide.X) + Math.Abs(at.Z - FieldEastSide.Z),
            Is.LessThan(0.5f),
            $"by the east wall, at {at}");
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
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

    // Creates the named character and enters the world with it.
    private static IEnumerator EnterByName(GameClient client, string name)
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
        server.Start(m_database, "--World:RandomSeed=13");
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
