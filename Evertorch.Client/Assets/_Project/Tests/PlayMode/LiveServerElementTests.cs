using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 18 "Elements, races, and sizes" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>ElementAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of what the real
///     client draws: the target frame's facts and hint, the Stats window's elements, and the status bar's converter; in
///     the Umbral Grotto, the Ghost's half, the Barrow Crawler's bite, and the Slime Monarch's slam on Earth armor.
/// </summary>
public sealed class LiveServerElementTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string VeraName = "LiveElementVera";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string WindConverter = "item.consumable.wind_converter";
    private const string TrainingSlime = "monster.training_slime";
    private const string ClothArmor = "item.armor.cloth";
    private const string GrottoCrawlerCard = "item.card.grotto_crawler";
    private const string UmbralGrotto = "map.umbral_grotto";
    private const string HollowWisp = "monster.hollow_wisp";
    private const string BarrowCrawler = "monster.barrow_crawler";
    private const string Monarch = "monster.slime_monarch";
    private const string GraveBite = "skill.grave_bite";
    private const string QuakeSlam = "skill.quake_slam";
    private const float StartTimeoutSeconds = 30f;
    private const float FightTimeoutSeconds = 60f;
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

    // The real client, stored wearing a training sword and holding a Wind Converter, enters the training ground: the
    // Stats window lists the derived statistics with the sword's attack and its Neutral elements, and a training slime,
    // selected, fills the target frame with its name, its level, its facts, and how far away it is. The converter,
    // used, makes the swing Wind: the frame says it fares half again against the Water slime, the status bar shows its
    // twenty minutes, and the chat says so (Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Fighter_WithASwordWorn_SelectsASlime_AndReadsItInTheTargetFrame()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, VeraName, () =>
        {
            StoreWorn(TrainingSword);
            StoreItem(WindConverter);
        });
        ClientWorld world = client.World!;
        Assert.That(
            world.Inventory.Rows.Select(row => (row.Item.Value, row.Slot)).OrderBy(row => row.Value),
            Is.EqualTo(new[] { (WindConverter, EquipmentSlot.None), (TrainingSword, EquipmentSlot.Weapon) }),
            "the stored sword, worn, and the converter");

        StatsWindow stats = client.GetComponentsInChildren<StatsWindow>(true).Single();
        yield return WaitUntil(() => world.Sheet != null, StartTimeoutSeconds);
        client.ToggleStats();
        yield return WaitUntil(() => stats.IsOpen && stats.Text.Contains("Attack"), StartTimeoutSeconds);
        Assert.That(stats.Text, Does.Contain("Attack").And.Contain("Defense"), $"the derived statistics: {stats.Text}");
        Assert.That(
            stats.Text,
            Does.Contain("Neutral attack").And.Contain("Neutral armor"),
            $"the elements: {stats.Text}");
        client.ToggleStats();

        yield return WaitUntil(
            () => world.Remotes.Values.Any(remote => remote.DefinitionId == TrainingSlime && !remote.IsDead),
            StartTimeoutSeconds);
        EntityId slime = world.Remotes.Values
            .First(remote => remote.DefinitionId == TrainingSlime && !remote.IsDead)
            .Entity;
        client.Connection!.SendTarget(slime);
        TargetFrame frame = client.GetComponentsInChildren<TargetFrame>(true).Single();
        TMP_Text name = frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Name");
        TMP_Text detail = frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Detail");
        yield return WaitUntil(
            () => world.Target == slime && frame.IsVisible && detail.text.EndsWith(" m", StringComparison.Ordinal),
            StartTimeoutSeconds);
        Assert.That(world.Target, Is.EqualTo(slime), $"the slime selected: {server.JoinOutput()}");
        Assert.That(name.text, Is.EqualTo("Training Slime · Lv 1"), "the slime's name and level");
        Assert.That(detail.text, Does.EndWith(" m"), "how far away it is, and a Neutral swing fares as ever");
        TMP_Text facts = frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Facts");
        Assert.That(facts.text, Is.EqualTo("Water · Plant · Medium"), "the slime's facts");

        long converter = world.Inventory.Rows.Single(row => row.Item.Value == WindConverter).InventoryItem;
        client.Connection.SendUseItem(converter);
        StatusBar bar = client.GetComponentsInChildren<StatusBar>(true).Single();
        TMP_Text effects = bar.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Effects");
        yield return WaitUntil(
            () => world.Sheet?.AttackElement == Element.Wind
                && detail.text.EndsWith(" m · Swing 150%", StringComparison.Ordinal)
                && effects.text.Length > 0,
            StartTimeoutSeconds);
        Assert.That(detail.text, Does.EndWith(" m · Swing 150%"), $"Wind on Water: {server.JoinOutput()}");
        Assert.That(effects.text, Is.EqualTo("Wind Weapon 20m"), "the converter's twenty minutes");
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Has.Member("Your attacks are now Wind."),
            "the chat says so");
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // The real client, stored in the wisp gallery's south-east corner wearing a training sword, selects a Hollow Wisp:
    // the target frame gives its facts and says a Neutral swing fares at half against the Ghost (Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Fighter_InTheWispGallery_ReadsAHollowWispAsAGhost()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, VeraName, () =>
        {
            PlaceInTheGrotto(22f, -26.5f);
            StoreWorn(TrainingSword);
        });
        ClientWorld world = client.World!;
        Assert.That(world.Map.Value, Is.EqualTo(UmbralGrotto), client.Status);

        yield return WaitUntil(() => Nearest(world, HollowWisp) != default, StartTimeoutSeconds);
        EntityId ghost = Nearest(world, HollowWisp);
        Assert.That(ghost, Is.Not.EqualTo(default(EntityId)), "the client draws a Hollow Wisp");
        client.Connection!.SendTarget(ghost);
        TargetFrame frame = client.GetComponentsInChildren<TargetFrame>(true).Single();
        TMP_Text facts = frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Facts");
        TMP_Text detail = frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Detail");
        yield return WaitUntil(
            () => world.Target == ghost
                && frame.IsVisible
                && detail.text.EndsWith(" m · Swing 50%", StringComparison.Ordinal),
            StartTimeoutSeconds);
        Assert.That(facts.text, Is.EqualTo("Ghost · Demon · Small"), "the ghost's facts");
        Assert.That(detail.text, Does.EndWith(" m · Swing 50%"), $"Neutral on a Ghost: {server.JoinOutput()}");
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // The real client, stored inside the Barrow Crawlers' spawn in the entrance hall wearing a training sword, strikes
    // one until a hit provokes it (a miss does not, Gameplay Systems §10); the pack turns on the player and bites with
    // Grave Bite, which the client hears as a crawler's skill on it. The test package disarms the crawlers, so a bite
    // strikes for 1, or for nothing when dodged.
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Fighter_BesideTheBarrowPack_IsBittenWithGraveBite()
    {
        string actionsPath = RequirePrerequisites();
        using var package = LiveTestPackage.WithMonster(
            BarrowCrawler,
            new Dictionary<string, string> { ["physicalAttack"] = "0" });
        yield return StartDatabaseAndServer(package.Path);
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, VeraName, () =>
        {
            PlaceInTheGrotto(-20.5f, -24f);
            StoreWorn(TrainingSword);
        });
        ClientWorld world = client.World!;
        yield return WaitUntil(() => Nearest(world, BarrowCrawler) != default, StartTimeoutSeconds);
        EntityId crawler = Nearest(world, BarrowCrawler);
        Assert.That(crawler, Is.Not.EqualTo(default(EntityId)), "the client draws a Barrow Crawler");

        bool isHit = false;
        SkillResolved? bite = null;
        world.DamageReceived += damage => isHit |= damage.Source == world.LocalEntity
            && damage.Target == crawler
            && damage.Result != CombatResult.Miss;
        world.SkillResolvedReceived += resolved =>
            bite ??= resolved.Skill.Value == GraveBite && resolved.Target == world.LocalEntity ? resolved : null;
        client.Connection!.SendAttack(crawler);
        float deadline = Time.realtimeSinceStartup + FightTimeoutSeconds;
        while (bite == null && Time.realtimeSinceStartup < deadline)
        {
            StepBeside(client, world, crawler);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        Assert.That(isHit, Is.True, $"the player hit the crawler: {server.JoinOutput()}");
        Assert.That(bite, Is.Not.Null, $"bitten: {server.JoinOutput()}");
        Assert.That(world.Remotes[bite!.Caster].DefinitionId, Is.EqualTo(BarrowCrawler), "by a crawler");
        Assert.That(bite.Amount, Is.LessThanOrEqualTo(1u), "the disarmed bite");
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // The real client, stored 2.5 m from the Slime Monarch wearing cloth armor carded Earthen, reads Earth armor in the
    // Stats window, and the boss's Earth slam strikes it for a quarter (Gameplay Systems §3, §9): the test package's
    // magic attack of 40 deals a Neutral slam of 38 to 57 before the magic defense, so a quarter is 14 at the most. Its
    // swings strike for 1.
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Fighter_InEarthenArmor_TakesAQuarterOfTheMonarchsSlam()
    {
        string actionsPath = RequirePrerequisites();
        using var package = LiveTestPackage.WithMonster(
            Monarch,
            new Dictionary<string, string> { ["physicalAttack"] = "0", ["magicAttack"] = "40" });
        yield return StartDatabaseAndServer(package.Path);
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        SkillResolved? slam = null;
        yield return EnterByName(client, VeraName, () =>
        {
            PlaceInTheGrotto(15.5f, 18f);
            StoreWorn(TrainingSword);
            StoreWornArmor(ClothArmor, GrottoCrawlerCard);
        });
        ClientWorld world = client.World!;
        world.SkillResolvedReceived += resolved =>
            slam ??= resolved.Skill.Value == QuakeSlam && resolved.Target == world.LocalEntity ? resolved : null;

        StatsWindow stats = client.GetComponentsInChildren<StatsWindow>(true).Single();
        yield return WaitUntil(() => world.Sheet?.ArmorElement == Element.Earth, StartTimeoutSeconds);
        client.ToggleStats();
        yield return WaitUntil(() => stats.IsOpen && stats.Text.Contains("Earth armor"), StartTimeoutSeconds);
        Assert.That(stats.Text, Does.Contain("Earth armor"), $"the Earthen armor: {stats.Text}");
        client.ToggleStats();

        yield return WaitUntil(() => slam != null, FightTimeoutSeconds);
        Assert.That(slam, Is.Not.Null, $"slammed: {server.JoinOutput()}");
        Assert.That(slam!.Outcome, Is.EqualTo(SkillOutcome.Hit), "the slam never misses");
        Assert.That(slam.Amount, Is.InRange(1u, 14u), "a quarter on Earth armor");
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    private static EntityId Nearest(ClientWorld world, string monster)
    {
        return world.Remotes.Values.FirstOrDefault(remote => remote.DefinitionId == monster && !remote.IsDead)?.Entity
            ?? default;
    }

    // A step to just west of where the client draws the monster, so the swing stays in reach while it moves.
    private static void StepBeside(GameClient client, ClientWorld world, EntityId monster)
    {
        if (world.Remotes.TryGetValue(monster, out RemoteEntity? remote)
            && remote.Buffer.TrySample(world.RemoteRenderTime, out WorldPosition at, out WorldDirection _))
        {
            client.Controller!.TryMoveTo(world.Predictor.Position, new WorldPosition(at.X - 1f, 0f, at.Z));
        }
    }

    private void PlaceInTheGrotto(float x, float z)
    {
        m_database!.Execute(
            $"UPDATE characters SET map_definition_id = '{UmbralGrotto}', position_x = {Number(x)}, position_y = 0, "
            + $"position_z = {Number(z)} WHERE name = '{VeraName}'");
    }

    private static string Number(float value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // The JSON is built in SQL, since the command line that carries the statement to the container drops quotes.
    private void StoreWornArmor(string armor, string card)
    {
        m_database!.Execute(
            "WITH worn AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"version, instance_data_json) SELECT id, '{armor}', 1, 0, 0, "
            + $"jsonb_build_object('cards', jsonb_build_array('{card}')) "
            + $"FROM characters WHERE name = '{VeraName}' RETURNING character_id, id) INSERT INTO equipment "
            + "(character_id, slot, inventory_item_id, version) SELECT character_id, 'Armor', id, 0 FROM worn");
    }

    private void StoreItem(string item)
    {
        m_database!.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT id, '{item}', 1, 0, 0 FROM characters WHERE name = '{VeraName}'");
    }

    private void StoreWorn(string weapon)
    {
        m_database!.Execute(
            "WITH worn AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"version) SELECT id, '{weapon}', 1, 0, 0 FROM characters WHERE name = '{VeraName}' "
            + "RETURNING character_id, id) INSERT INTO equipment (character_id, slot, inventory_item_id, version) "
            + "SELECT character_id, 'Weapon', id, 0 FROM worn");
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

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private IEnumerator StartDatabaseAndServer(string? contentPath = null)
    {
        Task<LiveDatabase> starting = LiveDatabase.StartAsync();
        yield return new WaitUntil(() => starting.IsCompleted);
        Assert.That(starting.IsFaulted, Is.False, starting.Exception?.GetBaseException().Message);
        m_database = starting.Result;

        LiveServer server = m_server = new LiveServer();
        server.Start(m_database, "--World:RandomSeed=18", contentPath);
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
