using System;
using System.Collections;
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
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;
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
    private const string StatsClientName = "LiveBuildTwo";
    private const string SkillsClientName = "LiveBuildThree";
    private const string ResetClientName = "LiveBuildFour";
    private const string ChangeClientName = "LiveBuildFive";
    private const string MendClientName = "LiveBuildSix";
    private const string MendedName = "LiveBuildSeven";
    private const string GuildmasterPrefab = "npc_guildmaster";
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

    // A character stored at base level 3, with 6 stat points, opens the Stats window with C and presses AGI's "+": the
    // server's sheet moves the row and the points, the feedback lines say so, and C closes the window (Gameplay
    // Systems §2; Prototype Content §2, §4).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Stats_RaisedThroughTheWindow_MoveWithTheServersSheet()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        GameClient client = CreateClient(actionsPath);

        yield return EnterByName(
            client,
            StatsClientName,
            () => database.Execute($"UPDATE characters SET base_level = 3 WHERE name = '{StatsClientName}'"));
        ClientWorld world = client.World!;
        StatsWindow window = client.GetComponentsInChildren<StatsWindow>(true).Single();
        FeedbackLines lines = client.GetComponentsInChildren<FeedbackLines>(true).Single();
        yield return WaitUntil(() => world.Sheet != null, StartTimeoutSeconds);
        Assert.That(world.Sheet?.StatPoints, Is.EqualTo((ushort)6), $"base level 3: {server.JoinOutput()}");

        yield return Tap(keyboard.cKey);
        yield return WaitUntil(() => window.IsOpen, 2f);
        Assert.That(window.IsOpen, Is.True, "C opens the window");
        Assert.That(window.Text, Does.Contain("AGI 5, next 2, +"));
        Button raise = window.GetComponentsInChildren<Button>().Single(button => button.name == "Raise AGI");
        raise.onClick.Invoke();
        yield return WaitUntil(() => window.Text.Contains("AGI 6, next 2, +"), StartTimeoutSeconds);

        Assert.That(world.Sheet!.Stats[1].Value, Is.EqualTo((byte)6), $"{client.Status} {server.JoinOutput()}");
        Assert.That(window.Text, Does.StartWith("Points: 4\n"), "2 points spent");
        Assert.That(window.Text, Does.Contain("AGI 6, next 2, +"));
        Assert.That(lines.Text, Does.Contain("AGI 6."));
        yield return Tap(keyboard.cKey);
        yield return WaitUntil(() => !window.IsOpen, 2f);
        Assert.That(window.IsOpen, Is.False, "C closes it again");
    }

    // A character stored at job level 2, with 1 skill point, opens the Skills window with K and learns Strike: the
    // server's list moves the row, the skill bar's first slot unlocks, and the feedback lines say so (Gameplay Systems
    // §9; Prototype Content §2, §4).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Skills_LearnedThroughTheWindow_UnlockTheirSlot()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        GameClient client = CreateClient(actionsPath);

        yield return EnterByName(
            client,
            SkillsClientName,
            () => database.Execute($"UPDATE characters SET job_level = 2 WHERE name = '{SkillsClientName}'"));
        ClientWorld world = client.World!;
        SkillsWindow window = client.GetComponentsInChildren<SkillsWindow>(true).Single();
        SkillBar bar = client.GetComponentsInChildren<SkillBar>(true).Single();
        FeedbackLines lines = client.GetComponentsInChildren<FeedbackLines>(true).Single();
        TMP_Text slot = bar.GetComponentsInChildren<Button>(true)
            .Single(button => button.name == "Slot 1")
            .GetComponentInChildren<TMP_Text>(true);
        yield return WaitUntil(() => world.Sheet != null && world.SkillsReceivedAt > 0, StartTimeoutSeconds);
        yield return WaitUntil(() => slot.text == "Strike\nLocked", 2f);
        Assert.That(slot.text, Is.EqualTo("Strike\nLocked"), "nothing learned yet");
        yield return Tap(keyboard.digit1Key);
        yield return null;
        Assert.That(lines.Text, Is.Empty, "a locked slot's key does nothing, not even ask for a target");

        yield return Tap(keyboard.kKey);
        yield return WaitUntil(() => window.IsOpen && window.Text.Contains("Strike Lv 0/5, Learn"), 2f);
        Assert.That(window.IsOpen, Is.True, "K opens the window");
        Assert.That(window.Text, Does.StartWith("Points: 1\nStrike Lv 0/5, Learn"), $"{server.JoinOutput()}");
        Button learn = window.GetComponentsInChildren<Button>().Single(button => button.name == "Learn skill.strike");
        learn.onClick.Invoke();
        yield return WaitUntil(
            () => slot.text == "Strike\n1" &&
                window.Text.StartsWith("Points: 0\nStrike Lv 1/5\n", StringComparison.Ordinal),
            StartTimeoutSeconds);

        Assert.That(slot.text, Is.EqualTo("Strike\n1"), $"the slot unlocks: {client.Status} {server.JoinOutput()}");
        Assert.That(window.Text, Does.StartWith("Points: 0\nStrike Lv 1/5\n"));
        Assert.That(lines.Text, Does.Contain("Strike Lv 1."));
        yield return Tap(keyboard.kKey);
        yield return WaitUntil(() => !window.IsOpen, 2f);
        Assert.That(window.IsOpen, Is.False, "K closes it again");
    }

    // A character stored at base level 3 with AGI raised to 7 walks up to the Guildmaster with a click, which offers it
    // no job change below the Adventurer's cap, presses "Reset all points" and then "Press again to reset": the server's
    // sheet gives the 4 points back, and the feedback lines say so (Gameplay Systems §6.1; Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Reset_ThroughTheGuildmastersWindow_ReturnsEveryPoint()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        GameClient client = CreateClient(actionsPath);

        yield return EnterByName(
            client,
            ResetClientName,
            () => database.Execute(
                $"UPDATE characters SET base_level = 3, agi = 7 WHERE name = '{ResetClientName}'"));
        ClientWorld world = client.World!;
        NpcWindow window = client.GetComponentsInChildren<NpcWindow>(true).Single();
        FeedbackLines lines = client.GetComponentsInChildren<FeedbackLines>(true).Single();
        yield return WaitUntil(() => world.Sheet != null, StartTimeoutSeconds);
        Assert.That(
            (world.Sheet!.Stats[1].Value, world.Sheet.StatPoints),
            Is.EqualTo(((byte)7, (ushort)2)),
            "AGI 7 cost 4 of the 6 points");
        yield return WaitUntil(() => NpcView(client, GuildmasterPrefab)?.HasBody == true, StartTimeoutSeconds);
        EntityView guildmaster = NpcView(client, GuildmasterPrefab)!;
        ClickAt(mouse,
            Camera.main!.WorldToScreenPoint(guildmaster.transform.position + Vector3.up * EntityPicker.PickHeight));
        const string offered = "\nReset all points\nChange job\nNeeds Adventurer Lv 10 to become an Arcanist"
            + "\nNeeds Adventurer Lv 10 to become a Vanguard";
        yield return WaitUntil(() => window.IsOpen && window.Text.EndsWith(offered), StartTimeoutSeconds);
        Assert.That(window.Text, Does.EndWith(offered), $"the Guildmaster's window: {client.Status}");

        Assert.That(Press(window, "Reset all points"), Is.True);
        yield return null;
        Assert.That(Press(window, "Press again to reset"), Is.True, window.Text);
        yield return WaitUntil(() => world.Sheet!.Stats[1].Value == 5, StartTimeoutSeconds);

        Assert.That(
            (world.Sheet!.Stats[1].Value, world.Sheet.StatPoints),
            Is.EqualTo(((byte)5, (ushort)6)),
            $"every point back: {server.JoinOutput()}");
        Assert.That(lines.Text, Does.Contain("Every point returned."));
    }

    // A character stored at Adventurer job level 10, whose nine skill points bought four levels, wearing the training
    // staff, walks up to the Guildmaster with a click and presses "Become a Vanguard" twice: the server's sheet names
    // the Vanguard at job level 1 with the five points carried, the status bar and the body follow it, the staff comes
    // off, and the feedback lines say so (Gameplay Systems §2.1, §6.1; Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator JobChange_ThroughTheGuildmastersWindow_DrawsTheVanguardAndTakesTheStaffOff()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        GameClient client = CreateClient(actionsPath);

        yield return EnterByName(
            client,
            ChangeClientName,
            () =>
            {
                database.SeedJob(
                    ChangeClientName,
                    "job.adventurer",
                    10,
                    ("skill.strike", 1),
                    ("skill.first_aid", 1),
                    ("skill.focus", 2));
                database.SeedWornWeapon(ChangeClientName, "item.weapon.training_staff");
            });
        ClientWorld world = client.World!;
        StatusBar bar = client.GetComponentsInChildren<StatusBar>(true).Single();
        TMP_Text shownName = bar.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Name");
        string shown = $"{ChangeClientName}   Lv 1   Adventurer Lv 10";
        yield return WaitUntil(() => shownName.text == shown, StartTimeoutSeconds);

        Assert.That(shownName.text, Is.EqualTo(shown), $"{client.Status} {server.JoinOutput()}");
        Assert.That(world.Sheet!.SkillPoints, Is.EqualTo(5), "four of the nine points spent");
        Assert.That(world.Inventory.Rows.Single().Slot, Is.EqualTo(EquipmentSlot.Weapon), "the staff worn");
        Assert.That(LocalBodyKey(client), Is.EqualTo("character_adventurer"));

        NpcWindow window = client.GetComponentsInChildren<NpcWindow>(true).Single();
        FeedbackLines lines = client.GetComponentsInChildren<FeedbackLines>(true).Single();
        yield return WaitUntil(() => NpcView(client, GuildmasterPrefab)?.HasBody == true, StartTimeoutSeconds);
        EntityView guildmaster = NpcView(client, GuildmasterPrefab)!;
        ClickAt(mouse,
            Camera.main!.WorldToScreenPoint(guildmaster.transform.position + Vector3.up * EntityPicker.PickHeight));
        yield return WaitUntil(() => window.IsOpen && window.Text.EndsWith("Become a Vanguard"), StartTimeoutSeconds);
        Assert.That(window.Text, Does.EndWith("\nChange job\nBecome an Arcanist\nBecome a Vanguard"), client.Status);
        Assert.That(Press(window, "Become a Vanguard"), Is.True);
        yield return null;
        Assert.That(Press(window, "Press again to become a Vanguard"), Is.True, window.Text);
        yield return WaitUntil(() => world.LocalJob.Value == "job.vanguard", StartTimeoutSeconds);

        string changed = $"{ChangeClientName}   Lv 1   Vanguard Lv 1";
        yield return WaitUntil(() => shownName.text == changed && LocalBodyKey(client) == "character_vanguard", 2f);
        Assert.That(
            (world.Sheet!.Job.Value, world.Sheet.JobLevel, world.Sheet.SkillPoints),
            Is.EqualTo(("job.vanguard", (ushort)1, (ushort)5)),
            $"the change: {server.JoinOutput()}");
        Assert.That(shownName.text, Is.EqualTo(changed), "the status bar names the Vanguard");
        Assert.That(LocalBodyKey(client), Is.EqualTo("character_vanguard"), "the Vanguard's body");
        Assert.That(world.Inventory.Rows.Single().Slot, Is.EqualTo(EquipmentSlot.None), "the staff taken off");
        Assert.That(lines.Text, Does.Contain("You are now a Vanguard."));
        Assert.That(lines.Text, Does.Not.Contain("Job level"), "nothing of the job level that fell");
    }

    // An Arcanist with Mend 1 clicks another player, which the server confirms: the target frame names its job alone,
    // and Mend then heals that player, as the healed player's own connection hears (Gameplay Systems §6, §9; Prototype
    // Content §2, §4).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Mend_OnAPlayerClickedOn_HealsItWhileTheFrameNamesItsJob()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        LiveDatabase database = m_database!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, server.JoinOutput());
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(
            client,
            MendClientName,
            () => database.SeedJob(
                MendClientName,
                "job.arcanist",
                1,
                ("skill.arcane_bolt", 1),
                ("skill.clarity", 1),
                ("skill.mend", 1)));
        ClientWorld world = client.World!;
        ClientContent content = client.Content!;

        // The healed player: the client's own networking without Unity's views, as a second window would run it.
        m_otherSocket = new LiteNetLibClientTransport("evertorch", 5000);
        var other = new ClientConnection(
            m_otherSocket,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, content.Version, "dev:livemend"),
            content);
        var picker = new CharacterPicker(MendedName);
        other.Connect("127.0.0.1", port);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                picker.Poll(other);
                return other.World?.Inventory.IsCurrent == true;
            },
            StartTimeoutSeconds);
        Assert.That(other.World, Is.Not.Null, $"{other.LocalError} {server.JoinOutput()}");
        ClientWorld otherWorld = other.World!;
        EntityId healed = otherWorld.LocalEntity;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.RemoteViews.TryGetValue(healed, out EntityView? view) && view.HasBody;
            },
            StartTimeoutSeconds);

        TargetFrame frame = client.GetComponentsInChildren<TargetFrame>(true).Single();
        ClickAt(
            mouse,
            Camera.main!.WorldToScreenPoint(
                client.RemoteViews[healed].transform.position + Vector3.up * EntityPicker.PickHeight));
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return world.Target == healed && frame.IsVisible;
            },
            StartTimeoutSeconds);
        Assert.That(world.Target, Is.EqualTo(healed), $"the click selected the player: {server.JoinOutput()}");
        yield return null;
        TMP_Text[] labels = frame.GetComponentsInChildren<TMP_Text>(true);
        Assert.That(
            (labels.Single(label => label.name == "Name").text, labels.Single(label => label.name == "Detail").text),
            Is.EqualTo(("Adventurer", string.Empty)),
            "the frame names the job alone");

        SkillResolved? seen = null;
        SkillResolved? heard = null;
        world.SkillResolvedReceived += resolved => seen = resolved.Target == healed ? resolved : seen;
        otherWorld.SkillResolvedReceived += resolved => heard = resolved;
        client.UseSkill(new SkillDefinitionId("skill.mend"));
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return seen != null && heard != null;
            },
            StartTimeoutSeconds);

        Assert.That(seen, Is.Not.Null, $"the caster saw the heal: {world.LastRejection} {server.JoinOutput()}");
        Assert.That(
            (seen!.Caster, seen.Outcome, seen.Amount),
            Is.EqualTo((world.LocalEntity, SkillOutcome.Healed, 40u)),
            "Mend 1 on the player");
        Assert.That((heard!.Caster, heard.Target), Is.EqualTo((world.LocalEntity, healed)),
            "the healed player heard it");
        Assert.That(world.Target, Is.EqualTo(healed), "still selected");
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the healed player's traffic");
    }

    private static string LocalBodyKey(GameClient client)
    {
        var view = (EntityView?)typeof(GameClient)
            .GetField("m_localView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client);
        return view != null ? view.Key : string.Empty;
    }

    private static bool Press(Component window, string name)
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
