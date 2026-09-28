using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 9 Adventurer build's presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>AdventurerBuildAcceptanceTests</c> hold every step; here each later line adds only a bounded check of what the
///     real client draws.
/// </summary>
public sealed class LiveServerBuildTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string LevelClientName = "LiveBuildOne";
    private const float StartTimeoutSeconds = 30f;
    private const float FightTimeoutSeconds = 60f;
    private const int TestTimeoutMs = 300_000;

    // Five short of the 30 the second base and job levels need, so the first slime (10 of each) raises both and leaves
    // 5 of the next 50.
    private const int SeededExperience = 25;
    private const float ExperienceAfterTheKill = 5f / 50f;

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

    // A new character, stored five experience short of its second base and job levels, kills one training slime with
    // Tab and the West button: the status bar shows both new levels and both bars what is left over (Gameplay Systems
    // §2.1; Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Levels_RisenByOneSlime_ShowOnTheStatusBar()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Assert.That(server.TryReadListeningPort(out int _), Is.True, $"server output: {server.JoinOutput()}");
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        GameClient client = CreateClient(actionsPath);

        yield return EnterByName(
            client,
            LevelClientName,
            () => database.Execute(
                $"UPDATE characters SET base_exp = {SeededExperience}, job_exp = {SeededExperience} "
                + $"WHERE name = '{LevelClientName}'"));
        ClientWorld world = client.World!;
        StatusBar bar = client.GetComponentsInChildren<StatusBar>(true).Single();
        TMP_Text shownName = bar.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Name");
        string before = $"{LevelClientName}   Lv 1   Adventurer Lv 1";
        yield return WaitUntil(() => shownName.text == before, StartTimeoutSeconds);
        Assert.That(shownName.text, Is.EqualTo(before), "a new character, once its sheet arrived");

        float deadline = Time.realtimeSinceStartup + FightTimeoutSeconds;
        while (world.Level < 2 && Time.realtimeSinceStartup < deadline)
        {
            if (world.Target == default)
            {
                yield return Tap(keyboard.tabKey);
                yield return WaitUntil(() => world.Target != default, 2f);
                if (world.Target != default)
                {
                    yield return Tap(gamepad.buttonWest);
                }
            }
            else
            {
                yield return null;
            }
        }

        Assert.That(world.Level, Is.EqualTo(2), $"{client.Status} {server.JoinOutput()}");
        string after = $"{LevelClientName}   Lv 2   Adventurer Lv 2";
        yield return WaitUntil(() => shownName.text == after, 2f);
        Assert.That(shownName.text, Is.EqualTo(after), "the status bar shows both new levels");
        Assert.That(bar.ShownExperienceRatio, Is.EqualTo(ExperienceAfterTheKill).Within(0.001f), "the left over");
        Assert.That(bar.ShownJobExperienceRatio, Is.EqualTo(ExperienceAfterTheKill).Within(0.001f), "the job's too");
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

    private IEnumerator Tap(ButtonControl button)
    {
        Press(button);
        yield return null;
        Release(button);
        yield return null;
    }
}
}
