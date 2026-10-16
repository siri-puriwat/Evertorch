using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;
using EquipmentSlot = Evertorch.Game.EquipmentSlot;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 20 "Equipment and visible clothing" presentation checks (ROADMAP §8; Coding Standards §10): the
///     real <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>EquipmentAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of what the
///     real client shows and draws.
/// </summary>
public sealed class LiveServerEquipmentTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string AnnName = "LiveGearAnn";
    private const string BobName = "LiveGearBob";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string ClothArmor = "item.armor.cloth";
    private const string LeatherArmor = "item.armor.leather";
    private const string TravelerCharm = "item.accessory.traveler_charm";
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

    // The real client, stored wearing a training sword with cloth armor and +4 leather armor in the bag, enters with
    // them; a press of the leather's row wears it, which the sheet counts with its refine bonus, and a press of the
    // cloth's row swaps it back in one equip. Another player of another account enters beside it, sees the sword in
    // its hand, and inspects it: the sword and the cloth (Gameplay Systems §11.1; Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Wearer_WithArmorInTheBag_WearsAndSwapsItByItsRows_AndIsInspected()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, server.JoinOutput());
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, AnnName, () =>
        {
            m_database!.SeedWornWeapon(AnnName, TrainingSword);
            StoreItem(ClothArmor, 0);
            StoreItem(LeatherArmor, 4);
        });
        ClientWorld world = client.World!;
        InventoryWindow bag = client.GetComponentsInChildren<InventoryWindow>(true).Single();
        yield return WaitUntil(() => Row(bag, "+4 Leather Armor [1]") != null, StartTimeoutSeconds);
        Assert.That(
            bag.Text.Split('\n').Where(line => line.Length > 0).OrderBy(line => line, StringComparer.Ordinal),
            Is.EqualTo(new[] { "+4 Leather Armor [1]", "Cloth Armor [1]", "Training Sword [1] (equipped)" }),
            "the stored bag, the sword worn");

        Row(bag, "+4 Leather Armor [1]")!.onClick.Invoke();
        yield return WaitUntil(() => Row(bag, "+4 Leather Armor [1] (equipped)") != null, StartTimeoutSeconds);
        Assert.That(Row(bag, "+4 Leather Armor [1] (equipped)"), Is.Not.Null,
            $"the leather worn: {world.LastRejection}");
        yield return WaitUntil(() => world.Sheet?.EquipmentDefense == 35, StartTimeoutSeconds);
        Assert.That(world.Sheet?.EquipmentDefense, Is.EqualTo(35), "the leather's 15 and its +4's 20");

        Row(bag, "Cloth Armor [1]")!.onClick.Invoke();
        yield return WaitUntil(() => Row(bag, "Cloth Armor [1] (equipped)") != null, StartTimeoutSeconds);
        Assert.That(Row(bag, "+4 Leather Armor [1]"), Is.Not.Null,
            $"the leather back in the bag: {world.LastRejection}");
        yield return WaitUntil(() => world.Sheet?.EquipmentDefense == 8, StartTimeoutSeconds);
        Assert.That(world.Sheet?.EquipmentDefense, Is.EqualTo(8), "the cloth alone");
        yield return WaitUntil(
            () => WornLook(client, EquipmentSlot.Armor) == "Socket_Chest worn_cloth_armor",
            StartTimeoutSeconds);
        Assert.That(
            WornLook(client, EquipmentSlot.Armor),
            Is.EqualTo("Socket_Chest worn_cloth_armor"),
            "the body wears the cloth's model");

        yield return EnterTheOther(client, port);
        ClientConnection other = m_other!;
        EntityId ann = world.LocalEntity;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.World!.Remotes.TryGetValue(ann, out RemoteEntity? seen)
                    && seen.WornWeapon == TrainingSword;
            },
            StartTimeoutSeconds);
        Assert.That(
            other.World!.Remotes.TryGetValue(ann, out RemoteEntity? remote) ? remote.WornWeapon : "not seen",
            Is.EqualTo(TrainingSword),
            $"the other player sees the sword in Ann's hand: {server.JoinOutput()}");

        // The bare connection puts the answer's parts together as the client does (Network Protocol §9).
        var parts = new InspectionParts();
        PlayerInspection? answer = null;
        Action<InspectedEquipment> take = part =>
        {
            if (parts.TryAdd(part, out PlayerInspection? whole))
            {
                answer = whole;
            }
        };
        other.InspectedEquipmentReceived += take;
        uint asked = other.SendInspectPlayer(ann);
        parts.Expect(asked, ann, Time.realtimeSinceStartupAsDouble);
        Assert.That(asked, Is.Not.Zero, "asked");
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return answer != null;
            },
            StartTimeoutSeconds);
        other.InspectedEquipmentReceived -= take;
        Assert.That(answer, Is.Not.Null, "answered");
        Assert.That(
            (answer!.ItemIn(EquipmentSlot.Weapon).Item.Value, answer.ItemIn(EquipmentSlot.Armor).Item.Value),
            Is.EqualTo((TrainingSword, ClothArmor)),
            "the sword and the cloth inspected");
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the other player's traffic");
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // The real client, stored with a traveler charm in the bag, presses its row: the Equipment window opens with the
    // charm chosen, and a press on accessory 2 wears it there, which the window and the bag show once the server
    // commits it (Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Wearer_PressesACharmsRow_AndWearsItAsAccessory2_InTheEquipmentWindow()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, AnnName, () => StoreItem(TravelerCharm, 0));
        ClientWorld world = client.World!;
        InventoryWindow bag = client.GetComponentsInChildren<InventoryWindow>(true).Single();
        EquipmentWindow equipment = client.GetComponentsInChildren<EquipmentWindow>(true).Single();
        yield return WaitUntil(() => Row(bag, "Traveler Charm [1]") != null, StartTimeoutSeconds);

        Row(bag, "Traveler Charm [1]")!.onClick.Invoke();
        yield return null;
        string chosen = equipment.Text;
        Row(equipment, "Accessory 2: empty")!.onClick.Invoke();
        yield return WaitUntil(() => Row(equipment, "Accessory 2: Traveler Charm [1]") != null, StartTimeoutSeconds);

        Assert.That(chosen, Does.Contain("\nWear Traveler Charm [1] in Accessory 1 or Accessory 2\n"), "chosen");
        Assert.That(
            Row(equipment, "Accessory 2: Traveler Charm [1]"),
            Is.Not.Null,
            $"worn as accessory 2: {world.LastRejection}");
        Assert.That(equipment.Text, Does.Contain("\nAccessory 1: empty\n"));
        Assert.That(Row(bag, "Traveler Charm [1] (equipped)"), Is.Not.Null, "the bag marks it worn");
        yield return WaitUntil(
            () => WornLook(client, EquipmentSlot.Accessory2) == "Socket_Hip_R worn_traveler_charm",
            StartTimeoutSeconds);
        Assert.That(
            WornLook(client, EquipmentSlot.Accessory2),
            Is.EqualTo("Socket_Hip_R worn_traveler_charm"),
            "the charm hangs at the right hip");
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // The socket the real client's own body hangs a position's first part from, and the worn model that part came
    // from, by its material; empty while it wears nothing there.
    private static string WornLook(GameClient client, EquipmentSlot position)
    {
        var body = (EntityView?)typeof(GameClient)
            .GetField("m_localView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client);
        GameObject? part = body != null ? body.WornParts(position).FirstOrDefault() : null;
        if (part == null)
        {
            return string.Empty;
        }

        string material = part.GetComponent<Renderer>().sharedMaterials
            .Select(shared => shared.name)
            .FirstOrDefault(name => name.StartsWith("Art_worn_", StringComparison.Ordinal)) ?? "Art_?_";
        return $"{part.transform.parent.name} {material.Substring(4, material.LastIndexOf('_') - 4)}";
    }

    private static Button? Row(Component window, string name)
    {
        return window.GetComponentsInChildren<Button>()
            .FirstOrDefault(button => button.name == name);
    }

    private void StoreItem(string item, int refineLevel)
    {
        m_database!.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT id, '{item}', 1, {refineLevel}, 0 FROM characters WHERE name = '{AnnName}'");
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
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, client.Content!.Version, "dev:live-gear"),
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
        server.Start(m_database, "--World:RandomSeed=17");
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
