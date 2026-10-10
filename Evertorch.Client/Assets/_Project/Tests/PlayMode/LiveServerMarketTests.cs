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
using UnityEngine.TestTools.Utils;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 15 "Market board" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>MarketAcceptanceTests</c> hold the server's steps; here a bounded check of what the real client draws: the
///     Broker's body and window, its board and listings, listing from the bag, the confirming presses, and the words. The
///     server sells a listing for ten minutes, so its time left reads in minutes.
/// </summary>
public sealed class LiveServerMarketTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string AnnName = "LiveMarketAnn";
    private const string BobName = "LiveMarketBob";
    private const string GloomDust = "item.material.gloom_dust";
    private const string Broker = "npc.broker";
    private const uint AnnCoins = 300;
    private const uint BobCoins = 500;
    private const float StartTimeoutSeconds = 30f;
    private const int TestTimeoutMs = 300_000;

    private static readonly Color BrokerTint = new Color32(0xB8, 0x86, 0x2B, 0xFF);

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

    // The real client, stored holding ten Gloom Dust and 300 coins, enters and shows them; another player, stored beside
    // the Broker with 500 coins, enters and is drawn. A walk through the controller up to the Broker, whose whole body
    // wears its colour, opens its window, as an arrival does, so an editor without focus, which drops a queued click,
    // cannot fail the step. A first press on the bag's dust shows the fee and a second lists four; the other player
    // buys two through its own connection, the seller hears of the sale, and the window reads the listing again; a
    // first press on it arms the early end, and a second collects the sales less the tax and the two left (Gameplay
    // Systems §11.5; Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Seller_ThroughTheBrokersWindow_ListsSellsToTheBuyer_AndCollects()
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
                + $"SELECT id, '{GloomDust}', 10, 0, 0 FROM characters WHERE name = '{AnnName}'");
            m_database.Execute($"UPDATE characters SET currency = {AnnCoins} WHERE name = '{AnnName}'");
        });
        ClientWorld world = client.World!;
        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnCoins), "the stored coins");
        Assert.That(
            world.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}"),
            Is.EqualTo(new[] { $"{GloomDust} x 10" }),
            "the stored bag");

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
        yield return WaitUntil(() => NpcView(client, Broker)?.HasBody == true, StartTimeoutSeconds);
        EntityView broker = NpcView(client, Broker)!;
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in broker.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(block);
            Assert.That(
                block.GetColor("_BaseColor"),
                Is.EqualTo(BrokerTint).Using(new ColorEqualityComparer(1e-3f)),
                $"{renderer.name}: the whole body in the Broker's colour");
        }

        EntityId npc = client.RemoteViews.Single(pair => pair.Value == broker).Key;
        Vector3 at = broker.transform.position;
        var beside = new WorldPosition(at.x - 1.5f, 0f, at.z);
        Assert.That(client.Controller!.TryMoveTo(world.Predictor.Position, beside), Is.True, "a way to the Broker");
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
                return window.IsOpen && window.Text.Contains("List Gloom Dust x 10");
            },
            StartTimeoutSeconds);
        Assert.That(window.ShownName, Is.EqualTo("Broker"), $"{window.Text} at {world.Predictor.Position}");
        Assert.That(
            window.Text,
            Does.Contain("Gloom Dust: 28 coins, none for sale").And.Contain("Monarch Mantle: 1000 coins, none for sale")
                .And.Contain("Nothing listed"));

        window.AmountInput!.text = "4";
        Assert.That(Press(window, "List Gloom Dust x 10"), Is.True, window.Text);
        yield return null;
        Assert.That(window.Text, Does.Contain("Press again to list Gloom Dust x 4 for a fee of 1 coin"));
        Assert.That(Press(window, "Press again to list Gloom Dust x 4 for a fee of 1 coin"), Is.True, window.Text);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return window.Text.Contains("Collect Gloom Dust: 4 left, 0 sold");
            },
            StartTimeoutSeconds);
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Has.Member("Listed Gloom Dust x 4 for a fee of 1 coin."),
            server.JoinOutput());
        Assert.That(window.Text, Does.Match(@"Collect Gloom Dust: 4 left, 0 sold, 0 coins waiting, \d+m \d+s left"));
        Assert.That(window.Text, Does.Contain("List Gloom Dust x 6"), "the rest of the stack");

        other.SendMarketBuy(npc, new ItemDefinitionId(GloomDust), 2, 28);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return window.Text.Contains("Collect Gloom Dust: 2 left, 2 sold, 51 coins waiting");
            },
            StartTimeoutSeconds);
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Has.Member("Your Gloom Dust x 2 sold for 56 coins."),
            $"{window.Text} {server.JoinOutput()}");
        Assert.That(window.Text, Does.Contain("Collect Gloom Dust: 2 left, 2 sold, 51 coins waiting"), "read again");
        Assert.That(
            (other.World.Inventory.Coins, other.World.Inventory.Rows.Single().Quantity),
            Is.EqualTo((BobCoins - 56, 2u)),
            "the buyer's coins and dust");

        string listing = window.GetComponentsInChildren<Button>()
            .Select(button => button.name)
            .Single(name => name.StartsWith("Collect Gloom Dust: 2 left", StringComparison.Ordinal));
        Assert.That(Press(window, listing), Is.True, window.Text);
        yield return null;
        Assert.That(window.Text, Does.Contain("Press again to end the Gloom Dust listing early"), "still selling");
        Assert.That(Press(window, "Press again to end the Gloom Dust listing early"), Is.True, window.Text);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.ChatLog.Lines.Any(line => line.Text == "Collected 51 coins and Gloom Dust x 2.")
                    && window.Text.Contains("Nothing listed");
            },
            StartTimeoutSeconds);
        yield return null;
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Has.Member("Collected 51 coins and Gloom Dust x 2."),
            server.JoinOutput());
        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnCoins - 1 + 51), "the fee paid and the net collected");
        Assert.That(world.Inventory.Rows.Single().Quantity, Is.EqualTo(8u), "the two left back in the bag");
        Assert.That(window.Text, Does.Contain("Nothing listed"));
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the other player's traffic");
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // Presses the window's button of that name, as a click on it does.
    private static bool Press(NpcWindow window, string name)
    {
        Button? button = window.GetComponentsInChildren<Button>().FirstOrDefault(candidate => candidate.name == name);
        button?.onClick.Invoke();
        return button != null;
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

    // The other player: the client's own networking without Unity's views, as a second window would run it.
    private IEnumerator EnterTheOther(GameClient client, int port)
    {
        m_otherSocket = new LiteNetLibClientTransport("evertorch", 5000);
        ClientConnection other = m_other = new ClientConnection(
            m_otherSocket,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, client.Content!.Version, "dev:live-market"),
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

        // Stored just south of the Broker, within its reach, so the buyer needs no walk.
        m_database!.Execute(
            $"UPDATE characters SET currency = {BobCoins}, position_x = 3.5, position_z = 3.0 WHERE name = '{BobName}'");
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
        server.Start(m_database, "--World:RandomSeed=15 --Market:ListingDurationMs=600000");
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
