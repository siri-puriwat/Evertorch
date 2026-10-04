using System;
using System.Collections;
using System.Collections.Generic;
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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 14 "Trade and storage" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>TradeAndStorageAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of what
///     the real client draws: the target frame's Trade, the prompt, the trade window, holding still, the words, and the
///     Storekeeper's window.
/// </summary>
public sealed class LiveServerTradeTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string AnnName = "LiveTradeAnn";
    private const string BobName = "LiveTradeBob";
    private const string SlimeGel = "item.material.slime_gel";
    private const string Storekeeper = "npc.storekeeper";
    private const uint AnnCoins = 300;
    private const float StartTimeoutSeconds = 30f;
    private const int TestTimeoutMs = 300_000;
    private static readonly Color StorekeeperTint = new Color32(0x3E, 0x7C, 0xB1, 0xFF);

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

    // The real client, stored holding ten Slime Gel and 300 coins, enters beside another player, selects it, and the
    // target frame names it with its buttons (Prototype Content §4).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Partner_BesideTheRealClient_IsSelectedInTheTargetFrame()
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
                + $"SELECT id, '{SlimeGel}', 10, 0, 0 FROM characters WHERE name = '{AnnName}'");
            m_database.Execute($"UPDATE characters SET currency = {AnnCoins} WHERE name = '{AnnName}'");
        });
        ClientWorld world = client.World!;
        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnCoins), "the stored coins");
        Assert.That(
            world.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}"),
            Is.EqualTo(new[] { $"{SlimeGel} x 10" }),
            "the stored bag");

        yield return EnterTheOther(client, port);
        ClientConnection other = m_other!;
        EntityId seen = other.World!.LocalEntity;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return world.Remotes.ContainsKey(seen);
            },
            StartTimeoutSeconds);
        Assert.That(world.Remotes.ContainsKey(seen), Is.True, $"{client.Status} {server.JoinOutput()}");

        TargetFrame frame = client.GetComponentsInChildren<TargetFrame>(true).Single();
        client.Connection!.SendTarget(seen);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return world.Target == seen && frame.IsVisible;
            },
            StartTimeoutSeconds);
        yield return null;
        Assert.That(
            frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Name").text,
            Is.EqualTo($"{BobName} · Adventurer"),
            "the frame names the partner");
        Assert.That(
            frame.GetComponentsInChildren<Button>().Select(button => button.name),
            Has.Member(TargetFrame.Invite).And.Member(TargetFrame.Trade),
            "Invite for a player outside any party, and Trade while no trade is open");
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the other player's traffic");
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // The other player asks; the real client's prompt asks the question and Accept opens the trade window, where the
    // amount field's number of a bag row is offered by pressing the row, coins by the coin field, and Lock and Trade
    // finish it once the other locks and confirms: the log says "Trade complete." and the bag holds what is left. While
    // the trade is open the client stands still (Prototype Content §2, §4; Gameplay Systems §16).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Trade_ThroughThePromptAndTheWindow_CompletesAndTheBagShowsIt()
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
                + $"SELECT id, '{SlimeGel}', 10, 0, 0 FROM characters WHERE name = '{AnnName}'");
            m_database.Execute($"UPDATE characters SET currency = {AnnCoins} WHERE name = '{AnnName}'");
        });
        ClientWorld world = client.World!;
        yield return EnterTheOther(client, port);
        ClientConnection other = m_other!;
        var heard = new List<string>();
        other.TradeEventReceived += message => heard.Add($"{message.Kind} {message.Name}");

        other.SendTradeRequest(AnnName);
        PartyInvitePrompt prompt = client.InvitePrompt!;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return prompt.IsVisible;
            },
            StartTimeoutSeconds);
        Assert.That(prompt.Text, Is.EqualTo($"{BobName} wants to trade with you."), server.JoinOutput());
        prompt.GetComponentsInChildren<Button>().Single(button => button.name == PartyInvitePrompt.Accept).onClick
            .Invoke();
        TradeWindow window = client.TradeWindow!;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return window.IsOpen && heard.Contains($"Opened {AnnName}");
            },
            StartTimeoutSeconds);
        Assert.That(window.IsOpen, Is.True, $"{client.Status} {server.JoinOutput()}");
        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Has.Member($"Trading with {BobName}."));

        // Held still: a walk asked of the controller goes nowhere while the trade is open.
        WorldPosition stood = world.Predictor.Position;
        client.Controller!.TryMoveTo(stood, new WorldPosition(stood.X + 3f, 0f, stood.Z));
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return false;
            },
            1f);
        Assert.That(world.Predictor.Position, Is.EqualTo(stood), "held still");

        window.AmountInput!.text = "4";
        InventoryEntry gel = world.Inventory.Rows.Single(row => row.Item.Value == SlimeGel);
        client.PressInventoryRow(gel);
        window.CoinsInput!.text = "100";
        window.GetComponentsInChildren<Button>().Single(button => button.name == TradeWindow.OfferCoins).onClick
            .Invoke();
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return world.Trade.Own.Coins == 100 && world.Trade.OfferedOf(gel.InventoryItem) == 4;
            },
            StartTimeoutSeconds);
        Assert.That(window.Text, Does.Contain("Slime Gel x 4").And.Contain("100 coins"), window.Text);
        Assert.That(
            client.GetComponentsInChildren<InventoryWindow>(true).Single().Text,
            Does.Contain("(offered x 4)"));

        window.GetComponentsInChildren<Button>().Single(button => button.name == TradeWindow.Lock).onClick.Invoke();
        other.SendTradeLock();
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return world.Trade.Own.IsLocked && world.Trade.Theirs.IsLocked;
            },
            StartTimeoutSeconds);
        yield return null;
        window.GetComponentsInChildren<Button>().Single(button => button.name == TradeWindow.Confirm).onClick
            .Invoke();
        other.SendTradeConfirm();
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.ChatLog.Lines.Any(line => line.Text == "Trade complete.");
            },
            StartTimeoutSeconds);

        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Has.Member("Trade complete."), server.JoinOutput());
        Assert.That(window.IsOpen, Is.False, "the window closes with the trade");
        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnCoins - 100));
        Assert.That(
            world.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}"),
            Is.EqualTo(new[] { $"{SlimeGel} x 6" }));
        Assert.That(other.World!.Inventory.Coins, Is.EqualTo(100u));
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the other player's traffic");
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // The real client walks up to the Storekeeper, drawn as the Quartermaster in its own colour, and its window opens,
    // reads storage, and shows the fee. The amount field's number of a bag row is stored by pressing the row, and of a
    // stored row taken back by pressing that one; the log says what moved, and only the committed changes move the
    // lists and the coins (Gameplay Systems §11.4; Prototype Content §2). The walk goes through the controller and the
    // window opens as an arrival opens it, so an editor without focus, which drops a queued click, cannot fail the step;
    // the town loop's live test clicks an NPC.
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Storage_ThroughTheStorekeepersWindow_StoresAndTakesBack()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, AnnName, () =>
        {
            m_database!.Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                + $"SELECT id, '{SlimeGel}', 10, 0, 0 FROM characters WHERE name = '{AnnName}'");
            m_database.Execute($"UPDATE characters SET currency = {AnnCoins} WHERE name = '{AnnName}'");
        });
        ClientWorld world = client.World!;
        NpcWindow window = client.GetComponentsInChildren<NpcWindow>(true).Single();

        yield return WaitUntil(() => StorekeeperView(client)?.HasBody == true, StartTimeoutSeconds);
        EntityView storekeeper = StorekeeperView(client)!;
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in storekeeper.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(block);
            Assert.That(
                block.GetColor("_BaseColor"),
                Is.EqualTo(StorekeeperTint).Using(new ColorEqualityComparer(1e-3f)),
                $"{renderer.name}: the whole body in the Storekeeper's colour");
        }

        EntityId keeper = client.RemoteViews.Single(pair => pair.Value == storekeeper).Key;
        Vector3 at = storekeeper.transform.position;
        var beside = new WorldPosition(at.x + 1.5f, 0f, at.z);
        Assert.That(client.Controller!.TryMoveTo(world.Predictor.Position, beside), Is.True,
            "a way to the Storekeeper");
        yield return WaitUntil(
            () => new Vector2(world.Predictor.Position.X - beside.X, world.Predictor.Position.Z - beside.Z).magnitude
                < 0.1f,
            StartTimeoutSeconds);
        window.Open(keeper);
        yield return WaitUntil(() => window.IsOpen && window.Text.Contains("Each deposit costs"), StartTimeoutSeconds);
        Assert.That(window.ShownName, Is.EqualTo("Storekeeper"), $"{window.Text} at {world.Predictor.Position}");
        Assert.That(
            window.Text,
            Does.Contain("Each deposit costs 20 coins").And.Contain("Nothing stored").And.Contain("Slime Gel x 10"));

        window.AmountInput!.text = "4";
        Assert.That(Press(window, "Slime Gel x 10"), Is.True, window.Text);
        yield return WaitUntil(
            () => client.ChatLog.Lines.Any(line => line.Text == "Deposited Slime Gel x 4 for 20 coins."),
            StartTimeoutSeconds);
        yield return null;
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Has.Member("Deposited Slime Gel x 4 for 20 coins."),
            server.JoinOutput());
        Assert.That(window.Text, Does.Contain("Slime Gel x 4").And.Contain("Slime Gel x 6"));

        window.AmountInput.text = "1";
        Assert.That(Press(window, "Slime Gel x 4"), Is.True, window.Text);
        yield return WaitUntil(
            () => client.ChatLog.Lines.Any(line => line.Text == "Withdrew Slime Gel."),
            StartTimeoutSeconds);
        yield return null;

        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Has.Member("Withdrew Slime Gel."));
        Assert.That(window.Text, Does.Contain("Slime Gel x 3").And.Contain("Slime Gel x 7"));
        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnCoins - 20));
        Assert.That(world.Storage.Rows.Single().Quantity, Is.EqualTo(3u));
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    private static EntityView? StorekeeperView(GameClient client)
    {
        ClientWorld? world = client.World;
        return world == null
            ? null
            : client.RemoteViews
                .Where(pair => world.Remotes.TryGetValue(pair.Key, out RemoteEntity? remote)
                    && remote.DefinitionId == Storekeeper)
                .Select(pair => pair.Value)
                .SingleOrDefault();
    }

    // Presses the window's button of that name, as a click on it does.
    private static bool Press(NpcWindow window, string name)
    {
        Button? button = window.GetComponentsInChildren<Button>().FirstOrDefault(candidate => candidate.name == name);
        button?.onClick.Invoke();
        return button != null;
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
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, client.Content!.Version, "dev:live-trade"),
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
        server.Start(m_database, "--World:RandomSeed=14");
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
