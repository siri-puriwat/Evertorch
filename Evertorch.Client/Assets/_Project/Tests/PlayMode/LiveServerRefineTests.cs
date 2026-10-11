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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 16 "Refining" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>RefiningAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of what the real
///     client draws: the Smith's section with its chances and costs, refines and their words, "+7" in the windows, and the
///     Stats window's equipment.
/// </summary>
public sealed class LiveServerRefineTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string AnnName = "LiveRefineAnn";
    private const string BobName = "LiveRefineBob";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string Quartermaster = "npc.quartermaster";
    private const string Smith = "npc.smith";
    private const string Emberstone = "item.material.emberstone";
    private const uint AnnCoins = 500;
    private const int SwordLevel = 3;
    private const float StartTimeoutSeconds = 30f;
    private const int TestTimeoutMs = 300_000;

    private LiveDatabase? m_database;
    private LiveServer? m_server;
    private GameObject? m_client;
    private InputActionAsset? m_actions;

    private LiteNetLibClientTransport? m_otherSocket;
    private ClientConnection? m_other;

    [UnityTearDown]
    public IEnumerator StopEverything()
    {
        if (m_client != null)
        {
            Object.Destroy(m_client);
            m_client = null;
        }

        m_other = null;
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

    // The real client, stored holding a training sword at +3 and 500 coins, enters and holds the sword at its level;
    // another player of another account enters beside it and is drawn; a walk through the controller up to the
    // Quartermaster opens its window, as an arrival does, so an editor without focus, which drops a queued click, cannot
    // fail the step (Gameplay Systems §6.1).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Refiner_WithARefinedSword_SeesTheOther_AndOpensATownWindow()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, server.JoinOutput());
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, AnnName, () =>
        {
            m_database!.Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                + $"SELECT id, '{TrainingSword}', 1, {SwordLevel}, 0 FROM characters WHERE name = '{AnnName}'");
            m_database.Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                + $"SELECT id, '{Emberstone}', 2, 0, 0 FROM characters WHERE name = '{AnnName}'");
            m_database.Execute($"UPDATE characters SET currency = {AnnCoins} WHERE name = '{AnnName}'");
        });
        ClientWorld world = client.World!;
        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnCoins), "the stored coins");
        Assert.That(
            world.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity} +{row.RefineLevel}")
                .OrderBy(row => row, StringComparer.Ordinal),
            Is.EqualTo(new[] { $"{Emberstone} x 2 +0", $"{TrainingSword} x 1 +{SwordLevel}" }),
            "the stored bag, the sword at its level");

        yield return EnterTheOther(client, port);
        ClientConnection other = m_other!;
        EntityId seen = other.World!.LocalEntity;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.RemoteViews.ContainsKey(seen);
            },
            StartTimeoutSeconds);
        Assert.That(client.RemoteViews.ContainsKey(seen), Is.True, $"{client.Status} {server.JoinOutput()}");

        NpcWindow window = client.GetComponentsInChildren<NpcWindow>(true).Single();
        yield return WaitUntil(() => NpcView(client, Quartermaster)?.HasBody == true, StartTimeoutSeconds);
        EntityView quartermaster = NpcView(client, Quartermaster)!;
        EntityId npc = client.RemoteViews.Single(pair => pair.Value == quartermaster).Key;
        Vector3 at = quartermaster.transform.position;
        var beside = new WorldPosition(at.x + 1.5f, 0f, at.z);
        Assert.That(client.Controller!.TryMoveTo(world.Predictor.Position, beside), Is.True,
            "a way to the Quartermaster");
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return new Vector2(world.Predictor.Position.X - beside.X, world.Predictor.Position.Z - beside.Z)
                    .magnitude < 0.1f;
            },
            StartTimeoutSeconds);
        window.Open(npc);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return window.IsOpen && window.ShownName == "Quartermaster";
            },
            StartTimeoutSeconds);
        Assert.That(window.ShownName, Is.EqualTo("Quartermaster"), $"{window.Text} at {world.Predictor.Position}");
        Assert.That(
            window.Text,
            Does.Contain($"+{SwordLevel} Training Sword x 1"),
            "the sword among what the shop buys, named with its level");

        // The Smith's section lists the sword with the next level's chance and cost; a press at 100 % refines it, and
        // the chat says so (Prototype Content §2).
        yield return WaitUntil(() => NpcView(client, Smith)?.HasBody == true, StartTimeoutSeconds);
        EntityView smithView = NpcView(client, Smith)!;
        EntityId smith = client.RemoteViews.Single(pair => pair.Value == smithView).Key;
        Vector3 smithAt = smithView.transform.position;
        var besideSmith = new WorldPosition(smithAt.x + 1.5f, 0f, smithAt.z);
        Assert.That(client.Controller!.TryMoveTo(world.Predictor.Position, besideSmith), Is.True, "a way to the Smith");
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return new Vector2(world.Predictor.Position.X - besideSmith.X,
                        world.Predictor.Position.Z - besideSmith.Z)
                    .magnitude < 0.1f;
            },
            StartTimeoutSeconds);
        window.Open(smith);
        string row = $"+{SwordLevel} Training Sword → +{SwordLevel + 1} · 100 % · 20 coins · Emberstone 1 (2 held)";
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return window.IsOpen && window.Text.Contains(row);
            },
            StartTimeoutSeconds);
        Assert.That(window.Text, Does.Contain(row), $"the Smith's section at {world.Predictor.Position}");
        Button? press = window.GetComponentsInChildren<Button>().FirstOrDefault(button => button.name == row);
        Assert.That(press, Is.Not.Null, window.Text);
        press!.onClick.Invoke();
        string refined = $"Refined Training Sword to +{SwordLevel + 1}.";
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.ChatLog.Lines.Any(line => line.Text == refined);
            },
            StartTimeoutSeconds);
        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Does.Contain(refined));
        Assert.That(
            world.Inventory.Rows.Single(entry => entry.Item.Value == TrainingSword).RefineLevel,
            Is.EqualTo((byte)(SwordLevel + 1)));
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the other player's traffic");
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    private static EntityView? NpcView(GameClient client, string definition)
    {
        ClientWorld? world = client.World;
        return world == null
            ? null
            : client.RemoteViews
                .Where(pair => world.Remotes.TryGetValue(pair.Key, out RemoteEntity? remote)
                    && remote.DefinitionId == definition)
                .Select(pair => pair.Value)
                .SingleOrDefault();
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

    // Creates the named character, stores what the test gives it, and enters the world with it.
    private static IEnumerator EnterByName(GameClient client, string name, Action seed)
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
        seed();
        client.EnterWorld(connection.Characters.Single(entry => entry.Name == name).Character);
        yield return WaitUntil(() => client.World?.Inventory.IsCurrent == true, StartTimeoutSeconds);
        Assert.That(client.World?.Inventory.IsCurrent, Is.True, client.Status);
    }

    // The other player, of another account: the client's own networking without Unity's views, as a second window
    // would run it.
    private IEnumerator EnterTheOther(GameClient client, int port)
    {
        m_otherSocket = new LiteNetLibClientTransport("evertorch", 5000);
        ClientConnection other = m_other = new ClientConnection(
            m_otherSocket,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, client.Content!.Version, "dev:live-refine"),
            client.Content!);
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
        other.CreateCharacter(BobName);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.Characters.Any(entry => entry.Name == BobName);
            },
            StartTimeoutSeconds);
        other.EnterWorld(other.Characters.Single(entry => entry.Name == BobName).Character);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.World?.Inventory.IsCurrent == true;
            },
            StartTimeoutSeconds);
        Assert.That(other.World, Is.Not.Null, other.LocalError);
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
        server.Start(m_database, "--World:RandomSeed=16");
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
