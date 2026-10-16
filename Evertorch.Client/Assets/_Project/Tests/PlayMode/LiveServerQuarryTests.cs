using System;
using System.Collections;
using System.Collections.Generic;
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
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 21 "Ashen Quarry" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>QuarryAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of what the real
///     client draws on the way north and in the quarry.
/// </summary>
public sealed class LiveServerQuarryTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string ClientName = "LiveQuarryOne";
    private const string FieldScene = "11_TrainingField";
    private const string QuarryScene = "13_AshenQuarry";
    private const string Smith = "npc.smith";
    private const string CinderCrawler = "monster.cinder_crawler";
    private const string FurnaceHeart = "monster.furnace_heart";
    private const string Unlit = "Universal Render Pipeline/Unlit";

    private const string Fallen = "The Furnace Heart has fallen. MVP: " + ClientName + ".";

    // A Vanguard at base level 25, the cap, gains none of the MVP's 3,750 experience.
    private const string Awarded = "You are the MVP: 10 Emberstone.";

    // A row of part 1 of the Smith's fees: the Training Slime Card sorts after the fourteen cards of part 0.
    private const string TakeOutTheSlimeCard = "Take out Training Slime Card · 200 coins";
    private const float StartTimeoutSeconds = 30f;
    private const float StepTimeoutSeconds = 20f;
    private const int TestTimeoutMs = 300_000;

    // The ground's portal before its east gate; the field under its north wall, two metres in front of its northern
    // gate, where an arrival from the quarry stands, and the portal before that gate; and the quarry's portal before its
    // south gate.
    private static readonly WorldPosition GroundEastPortal = new(22.2f, 0f, 0f);
    private static readonly WorldPosition UnderTheNorthWall = new(-12f, 0f, 20.5f);
    private static readonly WorldPosition FieldNorthPortal = new(-12f, 0f, 22.2f);
    private static readonly WorldPosition QuarrySouthPortal = new(0f, 0f, -38.2f);

    // In the quarry: the crossroads, the west road above the cinder basin, the north road, and 2.5 m south of the
    // Furnace Heart's home at (0, 28), inside its 5 m burst.
    private static readonly WorldPosition AtTheCrossroads = new(0f, 0f, -5f);
    private static readonly WorldPosition OnTheWestRoad = new(-16f, 0f, -5f);
    private static readonly WorldPosition UpTheNorthRoad = new(0f, 0f, 10f);
    private static readonly WorldPosition BeforeTheBoss = new(0f, 0f, 25.5f);

    // The balance baseline's Vanguard (owner decision 24) and the complete +0 dungeon set it wears.
    private static readonly string[] VanguardSkills =
    {
        "('skill.strike', 3)", "('skill.first_aid', 1)", "('skill.focus', 3)", "('skill.heavy_blow', 5)",
        "('skill.war_cry', 3)", "('skill.iron_guard', 3)"
    };

    private static readonly (string Item, string Slot)[] VanguardWorn =
    {
        ("item.weapon.iron_sword", "Weapon"), ("item.armor.leather", "Armor"),
        ("item.headgear.grotto_circlet", "Headgear"), ("item.shield.carapace", "Shield"),
        ("item.garment.gloom_cape", "Garment"), ("item.footwear.grotto_boots", "Footwear"),
        ("item.accessory.grotto_charm", "Accessory1"), ("item.accessory.grotto_charm", "Accessory2")
    };

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

    // A Vanguard of the balance baseline enters the training ground, where the Smith's fees reach it, crosses to the
    // field, walks up under its north wall, and goes through its northern gate into the quarry, warm and fogged, and
    // back (Gameplay Systems §4.2, §11.7; Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator North_TheRealClientGoesThroughTheFieldsNorthernGateIntoTheQuarryAndBack()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, ClientName, () => StoreTheVanguard(ClientName));
        ClientWorld ground = client.World!;
        Assert.That(ground.Sheet?.Job.Value, Is.EqualTo("job.vanguard"), "the stored job");

        RemoteEntity? smith = null;
        yield return WaitUntil(
            () => (smith = ground.Remotes.Values.FirstOrDefault(remote => remote.DefinitionId == Smith)) != null
                && ground.CardRemovalFeesOf(smith.Entity).Count > 0,
            StepTimeoutSeconds);
        Assert.That(smith, Is.Not.Null, "the Smith in view");
        Assert.That(ground.CardRemovalFeesOf(smith!.Entity).Count, Is.EqualTo(17), "the Smith's fees for every card");
        Assert.That(ground.DroppedFeeLists, Is.Zero, "no list of fees dropped");
        yield return OpenTheSmith(client, ground, smith.Entity);
        NpcWindow window = client.GetComponentsInChildren<NpcWindow>(true).Single();
        yield return WaitUntil(() => Row(window, TakeOutTheSlimeCard) != null, StepTimeoutSeconds);
        Assert.That(Row(window, TakeOutTheSlimeCard), Is.Not.Null, $"a fee of the second part: {window.Text}");
        window.Close();

        Assert.That(
            client.Controller!.TryMoveTo(ground.Predictor.Position, GroundEastPortal),
            Is.True,
            "a way into the ground's portal");
        yield return WaitUntil(
            () => client.World != null && client.World != ground && client.World.Inventory.IsCurrent,
            StepTimeoutSeconds);
        ClientWorld field = client.World!;
        Assert.That(field.Map.Value, Is.EqualTo("map.training_field"), $"{client.Status} {server.JoinOutput()}");
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(FieldScene));

        yield return WalkTo(client, field, UnderTheNorthWall, "under the north wall");
        yield return new WaitForSecondsRealtime(1f);
        Assert.That(client.World, Is.SameAs(field), "still on the field");
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(FieldScene), "the field's scene");

        Assert.That(
            client.Controller!.TryMoveTo(field.Predictor.Position, FieldNorthPortal),
            Is.True,
            "a way into the field's northern portal");
        yield return WaitUntil(
            () => client.World != null && client.World != field && client.World.Inventory.IsCurrent,
            StepTimeoutSeconds);
        ClientWorld quarry = client.World!;
        Scene quarryScene = SceneManager.GetActiveScene();
        Assert.That(quarry.Map.Value, Is.EqualTo("map.ashen_quarry"), $"{client.Status} {server.JoinOutput()}");
        Assert.That(quarryScene.name, Is.EqualTo(QuarryScene));
        Assert.That(RenderSettings.fog, Is.True, "the quarry's fog");
        Assert.That(
            quarryScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MapLook>())
                .Select(look => look.Palette.Count),
            Is.EqualTo(new[] { 6 }),
            "the quarry's look, a colour for every surface");

        Assert.That(
            client.Controller!.TryMoveTo(quarry.Predictor.Position, QuarrySouthPortal),
            Is.True,
            "a way into the quarry's southern portal");
        yield return WaitUntil(
            () => client.World != null && client.World != quarry && client.World.Inventory.IsCurrent,
            StepTimeoutSeconds);
        WorldPosition back = client.World!.Predictor.Position;
        Assert.That(client.World.Map.Value, Is.EqualTo("map.training_field"), client.Status);
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(FieldScene), "the field's scene again");
        Assert.That(
            Math.Abs(back.X - UnderTheNorthWall.X) + Math.Abs(back.Z - UnderTheNorthWall.Z),
            Is.LessThan(0.5f),
            $"under the north wall, at {back}");
    }

    // The real client meets the quarry's monsters and its boss (Prototype Content §2): a Cinder Crawler's body in the
    // basin; then, before the Furnace Heart, its burst's telegraph, an unlit ring of 5 m at its feet; the client brings
    // the boss down, and its chat log says so and says the most valuable player's ten Emberstones. The test package
    // leaves the boss 1 HP and no dodge, has its swings and its burst strike for 1, and takes its Ash Cloud off.
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Boss_TheRealClientDrawsTheBurstsTelegraph_AndLogsTheMvpsEmberstones()
    {
        string actionsPath = RequirePrerequisites();
        using var package = LiveTestPackage.WithMonster(
            FurnaceHeart,
            new Dictionary<string, string>
            {
                ["hp"] = "1",
                ["physicalAttack"] = "0",
                ["flee"] = "0",
                ["magicAttack"] = "0"
            },
            new[] { (FurnaceHeart, "skill.ash_cloud") },
            true);
        yield return StartDatabaseAndServer(package.Path);
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, ClientName, () => StoreTheVanguard(ClientName));
        yield return CrossIntoTheQuarry(client, server);
        ClientWorld quarry = client.World!;

        yield return WalkTo(client, quarry, AtTheCrossroads, "at the crossroads");
        yield return WalkTo(client, quarry, OnTheWestRoad, "on the west road");
        yield return WaitUntil(() => CinderView(client) != null, StepTimeoutSeconds);
        Assert.That(CinderView(client), Is.Not.Null, "a Cinder Crawler drawn in the basin below");
        Assert.That(
            CinderView(client)!.transform.Find("Pose/Scale").localScale.x,
            Is.EqualTo(1.15f).Within(1e-3f),
            "at its scale");

        yield return WalkTo(client, quarry, AtTheCrossroads, "back at the crossroads");
        yield return WalkTo(client, quarry, UpTheNorthRoad, "up the north road");
        yield return WalkTo(client, quarry, BeforeTheBoss, "before the boss");
        GameObject? ring = null;
        yield return WaitUntil(() => (ring = ShownTelegraph()) != null, StepTimeoutSeconds);
        Assert.That(ring, Is.Not.Null, $"the burst's telegraph: {server.JoinOutput()}");
        RemoteEntity boss = quarry.Remotes.Values.Single(remote => remote.DefinitionId == FurnaceHeart);
        Vector3 feet = client.RemoteViews[boss.Entity].transform.position;
        Vector3 drawn = ring!.transform.position;
        Assert.That(ring.GetComponent<MeshFilter>().sharedMesh.bounds.extents.x, Is.EqualTo(5f).Within(1e-3f));
        Assert.That(ring.GetComponent<MeshRenderer>().sharedMaterial.shader.name, Is.EqualTo(Unlit), "unlit");
        Assert.That(new Vector2(drawn.x - feet.x, drawn.z - feet.z).magnitude, Is.LessThan(0.1f), "at its feet");

        client.Connection!.SendAttack(boss.Entity);
        float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
        while (!HasLine(client, Fallen) && Time.realtimeSinceStartup < deadline)
        {
            if (client.RemoteViews.TryGetValue(boss.Entity, out EntityView? view) && view != null)
            {
                Vector3 at = view.transform.position;
                client.Controller!.TryMoveTo(quarry.Predictor.Position, new WorldPosition(at.x, 0f, at.z - 1.2f));
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }

        Assert.That(HasLine(client, Fallen), Is.True, server.JoinOutput());
        yield return WaitUntil(() => HasLine(client, Awarded), StepTimeoutSeconds);
        Assert.That(HasLine(client, Awarded), Is.True, server.JoinOutput());
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // Walks up beside the Smith and opens its window.
    private static IEnumerator OpenTheSmith(GameClient client, ClientWorld world, EntityId smith)
    {
        NpcWindow window = client.GetComponentsInChildren<NpcWindow>(true).Single();
        yield return WaitUntil(() => client.RemoteViews.ContainsKey(smith), StepTimeoutSeconds);
        Vector3 at = client.RemoteViews[smith].transform.position;
        yield return WalkTo(client, world, new WorldPosition(at.x + 1.5f, 0f, at.z), "beside the Smith");
        window.Open(smith);
        yield return WaitUntil(() => window.IsOpen && window.ShownName == "Smith", StepTimeoutSeconds);
        Assert.That(window.ShownName, Is.EqualTo("Smith"), window.Text);
    }

    // Walks from the training ground across the field into the quarry, whose scene and world it then holds.
    private IEnumerator CrossIntoTheQuarry(GameClient client, LiveServer server)
    {
        ClientWorld ground = client.World!;
        Assert.That(client.Controller!.TryMoveTo(ground.Predictor.Position, GroundEastPortal), Is.True, "east");
        yield return WaitUntil(
            () => client.World != null && client.World != ground && client.World.Inventory.IsCurrent,
            StepTimeoutSeconds);
        ClientWorld field = client.World!;
        yield return WalkTo(client, field, UnderTheNorthWall, "under the north wall");
        Assert.That(client.Controller!.TryMoveTo(field.Predictor.Position, FieldNorthPortal), Is.True, "north");
        yield return WaitUntil(
            () => client.World != null && client.World != field && client.World.Inventory.IsCurrent,
            StepTimeoutSeconds);
        Assert.That(client.World!.Map.Value, Is.EqualTo("map.ashen_quarry"), $"{client.Status} {server.JoinOutput()}");
    }

    private static bool HasLine(GameClient client, string text)
    {
        return client.ChatLog.Lines.Any(line => line.Text == text);
    }

    private static Button? Row(Component window, string name)
    {
        return window.GetComponentsInChildren<Button>().FirstOrDefault(button => button.name == name);
    }

    private static GameObject? ShownTelegraph()
    {
        return Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
            .Select(filter => filter.gameObject)
            .FirstOrDefault(ring => ring.name == AreaTelegraphPresenter.ObjectName && ring.activeInHierarchy);
    }

    private static EntityView? CinderView(GameClient client)
    {
        RemoteEntity? remote = client.World?.Remotes.Values
            .FirstOrDefault(entity => entity.DefinitionId == CinderCrawler);
        return remote != null && client.RemoteViews.TryGetValue(remote.Entity, out EntityView? view) ? view : null;
    }

    private void StoreTheVanguard(string name)
    {
        LiveDatabase database = m_database!;
        database.Execute(
            "UPDATE characters SET base_level = 25, job_definition_id = 'job.vanguard', job_level = 10, job_exp = 0, "
            + "str = 25, agi = 8, vit = 15, int = 5, dex = 20, luk = 5, hp = 9999, sp = 9999 "
            + $"WHERE name = '{name}'");
        database.Execute(
            "INSERT INTO character_skills (character_id, skill_definition_id, level) SELECT c.id, s.skill, s.level "
            + $"FROM characters c, (VALUES {string.Join(", ", VanguardSkills)}) AS s(skill, level) "
            + $"WHERE c.name = '{name}'");
        // The JSON is built in SQL, since the command line that carries the statement to the container drops quotes.
        database.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, instance_data_json, "
            + "version) SELECT id, 'item.armor.cloth', 1, 0, "
            + "jsonb_build_object('cards', jsonb_build_array('item.card.training_slime')), 0 "
            + $"FROM characters WHERE name = '{name}'");
        foreach ((string item, string slot) in VanguardWorn)
        {
            database.Execute(
                "WITH worn AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, "
                + $"refine_level, version) SELECT id, '{item}', 1, 0, 0 FROM characters WHERE name = '{name}' "
                + "RETURNING character_id, id) INSERT INTO equipment (character_id, slot, inventory_item_id, version) "
                + $"SELECT character_id, '{slot}', id, 0 FROM worn");
        }
    }

    private static IEnumerator WalkTo(GameClient client, ClientWorld world, WorldPosition destination, string where)
    {
        Assert.That(client.Controller!.TryMoveTo(world.Predictor.Position, destination), Is.True, $"a way {where}");
        yield return WaitUntil(
            () => client.Controller != null && !client.Controller.HasPath && world.Predictor.PendingCount == 0,
            StepTimeoutSeconds);
        WorldPosition at = world.Predictor.Position;
        Assert.That(
            Math.Abs(at.X - destination.X) + Math.Abs(at.Z - destination.Z),
            Is.LessThan(0.5f),
            $"{where}, at {at}");
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

    // The seed runs between the character's creation and its entry: the server reads the stored row as it enters.
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
        server.Start(m_database, "--World:RandomSeed=21", contentPath ?? LiveServer.ContentPath);
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
