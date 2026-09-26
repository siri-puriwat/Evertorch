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
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;
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
    private const string NpcClientName = "LiveTownTwo";
    private const string CoinsClientName = "LiveTownThree";
    private const string ShopClientName = "LiveTownFour";
    private const long SeededCoins = 4321;
    private const string GelItem = "item.material.slime_gel";
    private const string SwordItem = "item.weapon.training_sword";
    private const string QuartermasterPrefab = "npc_quartermaster";
    private const string GateWardenPrefab = "npc_gate_warden";
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

    // Both NPCs are drawn from their prefabs, each standing on the plinth over its marker (Prototype Content §5). A
    // click on the Quartermaster walks the player up to it and opens its window, which closes once the Quartermaster is
    // drawn beyond 3 m. The click talks and never attacks: the server refuses nothing (Gameplay Systems §6.1).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Npcs_DrawnOnTheirPlinths_AndAClickOnTheQuartermasterWalksUpAndOpensItsWindow()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, $"server output: {server.JoinOutput()}");
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        GameClient client = CreateClient(port, actionsPath);
        yield return EnterByName(client, NpcClientName);
        ClientWorld town = client.World!;
        NpcWindow window = client.GetComponentsInChildren<NpcWindow>(true).Single();

        yield return WaitUntil(
            () => NpcView(client, QuartermasterPrefab)?.HasBody == true
                && NpcView(client, GateWardenPrefab)?.HasBody == true,
            StepTimeoutSeconds);
        foreach (string prefab in new[] { QuartermasterPrefab, GateWardenPrefab })
        {
            EntityView? view = NpcView(client, prefab);
            Assert.That(view, Is.Not.Null, $"{prefab}: drawn");
            Assert.That(view!.IsPlaceholder, Is.False, $"{prefab}: the body is its prefab, not the placeholder");
            Assert.That(
                view.transform.position.y,
                Is.EqualTo(GrayboxMeshBuilder.NpcMarkerHeight).Within(1e-4f),
                $"{prefab}: on its plinth");
        }

        EntityView quartermaster = NpcView(client, QuartermasterPrefab)!;
        EntityId npc = client.RemoteViews.Single(pair => pair.Value == quartermaster).Key;
        Vector3 onScreen = Camera.main!.WorldToScreenPoint(
            quartermaster.transform.position + Vector3.up * EntityPicker.PickHeight);
        Assert.That(
            onScreen.z > 0f && onScreen.x > 0f && onScreen.x < Screen.width && onScreen.y > 0f
            && onScreen.y < Screen.height,
            Is.True,
            $"the Quartermaster is on screen at {onScreen}");
        ClickAt(mouse, onScreen);
        yield return WaitUntil(() => window.IsOpen, StepTimeoutSeconds);
        Assert.That(window.IsOpen, Is.True, $"the window opened; the player at {town.Predictor.Position}");
        Assert.That(window.Npc, Is.EqualTo(npc));
        Assert.That(window.ShownName, Is.EqualTo("Quartermaster"));
        Assert.That(town.Target, Is.EqualTo(default(EntityId)), "the click selected nothing");
        Assert.That(DistanceToDrawn(town, quartermaster), Is.LessThanOrEqualTo(TalkState.OpenDistance), "beside it");

        // The console republishes what it reads once a second.
        yield return new WaitForSecondsRealtime(1.5f);
        long character = client.Connection!.Characters.Single(entry => entry.Name == NpcClientName).Character.Value;
        server.ClearOutput();
        server.SendCommand("players");
        yield return WaitUntil(() => server.HasOutput(LiveServer.CharacterMarker(character)), StepTimeoutSeconds);
        string line = server.Output().First(text => text.Contains(LiveServer.CharacterMarker(character)));
        Assert.That(line, Does.Contain(" refused 0 level "), "no AttackEntity was sent: the server refused nothing");

        Assert.That(
            client.Controller!.TryMoveTo(town.Predictor.Position, new WorldPosition(0f, 0f, 0f)),
            Is.True,
            "a way back to the spawn point");
        yield return WaitUntil(() => !window.IsOpen, StepTimeoutSeconds);
        Assert.That(window.IsOpen, Is.False, "the window closed on the walk away");
        Assert.That(DistanceToDrawn(town, quartermaster), Is.GreaterThan(NpcInteraction.Range), "past 3 m");
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // Nothing earns coins before the shop and the quest, so the test writes them to the database before the character
    // enters; the inventory window shows them above the rows, and the console's players line ends with them
    // (Prototype Content §2; System Architecture §10).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Coins_WrittenToTheDatabase_ShowInTheInventoryWindowAndOnTheConsole()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, $"server output: {server.JoinOutput()}");
        GameClient client = CreateClient(port, actionsPath);

        yield return EnterByName(
            client,
            CoinsClientName,
            () => database.Execute(
                $"UPDATE characters SET currency = {SeededCoins} WHERE name = '{CoinsClientName}'"));
        InventoryWindow window = client.GetComponentsInChildren<InventoryWindow>(true).Single();
        yield return WaitUntil(() => window.CoinsText == $"Coins: {SeededCoins}", StepTimeoutSeconds);

        Assert.That(window.CoinsText, Is.EqualTo($"Coins: {SeededCoins}"));
        Assert.That(client.World!.Inventory.Coins, Is.EqualTo((uint)SeededCoins));

        // The console republishes what it reads once a second.
        yield return new WaitForSecondsRealtime(1.5f);
        long character = client.Connection!.Characters.Single(entry => entry.Name == CoinsClientName).Character.Value;
        server.ClearOutput();
        server.SendCommand("players");
        yield return WaitUntil(() => server.HasOutput(LiveServer.CharacterMarker(character)), StepTimeoutSeconds);
        string line = server.Output().First(text => text.Contains(LiveServer.CharacterMarker(character)));
        Assert.That(line, Does.EndWith($" coins {SeededCoins}"));
    }

    // The Quartermaster's window over the real server (Gameplay Systems §11.3; Prototype Content §2), with coins and
    // a stack of gel written to the database before the character enters: a Buy press buys one training sword, a Sell
    // press sells one gel, and All sells the rest. Each committed change moves the coins and the lists and says what
    // happened, and the console's players line ends with the coins left.
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Shop_BuysWithSeededCoins_AndSellsASeededStack_ThroughTheQuartermastersWindow()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, $"server output: {server.JoinOutput()}");
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        GameClient client = CreateClient(port, actionsPath);
        yield return EnterByName(
            client,
            ShopClientName,
            () =>
            {
                database.Execute($"UPDATE characters SET currency = 100 WHERE name = '{ShopClientName}'");
                database.Execute(
                    "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                    + $"SELECT id, '{GelItem}', 6, 0, 0 FROM characters WHERE name = '{ShopClientName}'");
            });
        ClientWorld town = client.World!;
        NpcWindow window = client.GetComponentsInChildren<NpcWindow>(true).Single();
        FeedbackLines lines = client.GetComponentsInChildren<FeedbackLines>(true).Single();
        yield return WalkUpToTheQuartermaster(client, mouse, window);
        yield return WaitUntil(() => window.CoinsText == "Coins: 100", StepTimeoutSeconds);
        Assert.That(window.CoinsText, Is.EqualTo("Coins: 100"), window.Text);

        Assert.That(Press(window, "Training Sword: 50 coins"), Is.True, window.Text);
        yield return WaitUntil(() => town.Inventory.Coins == 50, StepTimeoutSeconds);
        Assert.That(town.Inventory.Coins, Is.EqualTo(50u), $"bought: {town.LastRejection}");
        Assert.That(lines.Text, Does.Contain("Bought Training Sword for 50 coins."));
        Assert.That(town.Inventory.Rows.Select(row => row.Item.Value), Does.Contain(SwordItem));

        yield return WaitUntil(() => window.Text.Contains("Slime Gel x 6"), StepTimeoutSeconds);
        Assert.That(Press(window, "Slime Gel x 6: 2 coins each"), Is.True, window.Text);
        yield return WaitUntil(() => town.Inventory.Coins == 52, StepTimeoutSeconds);
        Assert.That(town.Inventory.Coins, Is.EqualTo(52u), $"sold one: {town.LastRejection}");
        Assert.That(lines.Text, Does.Contain("Sold Slime Gel for 2 coins."));

        yield return WaitUntil(() => window.Text.Contains("Slime Gel x 5"), StepTimeoutSeconds);
        Assert.That(Press(window, "All: 10 coins"), Is.True, window.Text);
        yield return WaitUntil(() => town.Inventory.Coins == 62, StepTimeoutSeconds);
        Assert.That(town.Inventory.Coins, Is.EqualTo(62u), $"sold the rest: {town.LastRejection}");
        Assert.That(lines.Text, Does.Contain("Sold Slime Gel x 5 for 10 coins."));
        Assert.That(town.Inventory.Rows.Select(row => row.Item.Value), Has.No.Member(GelItem));
        yield return WaitUntil(() => window.CoinsText == "Coins: 62", StepTimeoutSeconds);
        Assert.That(window.Text, Does.EndWith("Sell\nTraining Sword x 1: 25 coins"), "the bought sword can be sold");

        // The console republishes what it reads once a second.
        yield return new WaitForSecondsRealtime(1.5f);
        long character = client.Connection!.Characters.Single(entry => entry.Name == ShopClientName).Character.Value;
        server.ClearOutput();
        server.SendCommand("players");
        yield return WaitUntil(() => server.HasOutput(LiveServer.CharacterMarker(character)), StepTimeoutSeconds);
        string line = server.Output().First(text => text.Contains(LiveServer.CharacterMarker(character)));
        Assert.That(line, Does.EndWith(" coins 62"));
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // A click on the Quartermaster walks the player up to it and opens its window.
    private static IEnumerator WalkUpToTheQuartermaster(GameClient client, Mouse mouse, NpcWindow window)
    {
        yield return WaitUntil(() => NpcView(client, QuartermasterPrefab)?.HasBody == true, StepTimeoutSeconds);
        EntityView quartermaster = NpcView(client, QuartermasterPrefab)!;
        ClickAt(
            mouse,
            Camera.main!.WorldToScreenPoint(quartermaster.transform.position + Vector3.up * EntityPicker.PickHeight));
        yield return WaitUntil(() => window.IsOpen, StepTimeoutSeconds);
        Assert.That(window.IsOpen, Is.True, "the Quartermaster's window opened");
    }

    // Presses the window's button of that name, as a click on it does.
    private static bool Press(NpcWindow window, string name)
    {
        Button? button = window.GetComponentsInChildren<Button>().FirstOrDefault(candidate => candidate.name == name);
        button?.onClick.Invoke();
        return button != null;
    }

    private static EntityView? NpcView(GameClient client, string prefab)
    {
        ClientWorld? world = client.World;
        return world == null
            ? null
            : client.RemoteViews
                .Where(pair => world.Remotes.TryGetValue(pair.Key, out RemoteEntity? remote)
                    && remote.Kind == EntityKind.Npc
                    && pair.Value.Key == prefab)
                .Select(pair => pair.Value)
                .SingleOrDefault();
    }

    // From the predicted player to where the view is drawn, as the NPC window measures.
    private static float DistanceToDrawn(ClientWorld world, EntityView view)
    {
        Vector3 at = view.transform.position;
        return new Vector2(at.x - world.Predictor.Position.X, at.z - world.Predictor.Position.Z).magnitude;
    }

    private static void ClickAt(Mouse mouse, Vector2 screenPosition)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPosition }.WithButton(MouseButton.Left));
        InputSystem.Update();
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPosition });
        InputSystem.Update();
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
