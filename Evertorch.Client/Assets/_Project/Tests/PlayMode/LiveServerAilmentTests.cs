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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 19 "Status ailments" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database, which the owner's walk drives with
///     a raised ailment chance. The headless <c>AilmentAcceptanceTests</c> hold the server's steps; here each later line
///     adds a bounded check of what the real client draws: the looks, the words, the status bar, and the skill bar.
/// </summary>
public sealed class LiveServerAilmentTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string InesName = "LiveAilmentInes";
    private const string AshStaff = "item.weapon.ash_staff";
    private const string ClarityStatus = "status.clarity";
    private const float StartTimeoutSeconds = 30f;
    private const int TestTimeoutMs = 300_000;
    private const float FightTimeoutSeconds = 60f;

    private static readonly SkillDefinitionId Clarity = new("skill.clarity");
    private static readonly SkillDefinitionId FrostLance = new("skill.frost_lance");

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

    // The real client, stored as a level-25 Arcanist wearing the Ash Staff, enters the training ground and raises
    // Clarity: the status bar names the effect with its seconds left (Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Arcanist_RaisesClarity_AndReadsItOnTheStatusBar()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, InesName, () =>
        {
            m_database!.SeedJob(
                InesName,
                "job.arcanist",
                10,
                ("skill.arcane_bolt", 5),
                ("skill.clarity", 1),
                ("skill.mend", 3));
            m_database.Execute($"UPDATE characters SET base_level = 25, sp = 100000 WHERE name = '{InesName}'");
            StoreWorn(AshStaff);
        });
        ClientWorld world = client.World!;
        Assert.That(
            world.Inventory.Rows.Select(row => (row.Item.Value, row.Slot)),
            Is.EqualTo(new[] { (AshStaff, EquipmentSlot.Weapon) }),
            "the stored staff, worn");

        client.Connection!.SendUseSkill(Clarity, default);
        StatusBar bar = client.GetComponentsInChildren<StatusBar>(true).Single();
        TMP_Text effects = bar.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Effects");
        yield return WaitUntil(
            () => world.StatusEffects.Any(effect => effect.Status.Value == ClarityStatus) && effects.text.Length > 0,
            StartTimeoutSeconds);
        Assert.That(
            world.StatusEffects.Select(effect => effect.Status.Value),
            Has.Member(ClarityStatus),
            $"Clarity raised: {world.LastRejection} {server.JoinOutput()}");
        Assert.That(effects.text, Does.Match("^Clarity (59|60)s$"), "the status bar's seconds");
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    // The real client, stored as an Arcanist with Frost Lance, frozen for a few seconds, poisoned, and blinded, enters the
    // training ground: the status bar lists the three ailments first at 12 points, the vignette darkens the view, and a
    // press of 9 says the character is frozen. Once the freeze wears off the bar's nine slots fit, Frost Lance on 9,
    // and on the gamepad's second page the d-pad's right casts it at a slime (Prototype Content §2, §4).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Arcanist_HeldByAFreeze_SaysWhy_ThenCastsFrostLanceFromSlotNine_ByTheGamepad()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, InesName, () =>
        {
            m_database!.SeedJob(
                InesName,
                "job.arcanist",
                10,
                ("skill.arcane_bolt", 5),
                ("skill.clarity", 1),
                ("skill.mend", 3),
                ("skill.frost_lance", 1));
            m_database.Execute($"UPDATE characters SET base_level = 25, sp = 100000 WHERE name = '{InesName}'");
            StoreWorn(AshStaff);
            StoreStatus("status.frozen", 6_000);
            StoreStatus("status.poisoned", 60_000);
            StoreStatus("status.blinded", 60_000);
        });
        ClientWorld world = client.World!;
        StatusBar status = client.GetComponentsInChildren<StatusBar>(true).Single();
        TMP_Text effects = status.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Effects");
        BlindVignette vignette = client.GetComponentsInChildren<BlindVignette>(true).Single();
        yield return WaitUntil(() => world.IsHeld && effects.text.Contains("Frozen"), StartTimeoutSeconds);

        Assert.That(world.IsHeld, Is.True, $"held by the stored freeze: {server.JoinOutput()}");
        Assert.That(effects.text, Does.Contain("Poisoned").And.Contain("Frozen").And.Contain("Blinded"));
        Assert.That(effects.fontSize, Is.EqualTo(12f));
        Assert.That(vignette.IsShown, Is.True, "blinded, the view darkens");

        yield return Tap(keyboard.digit9Key);
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Has.Member("You can't use skills while frozen."),
            "a press while frozen says why");

        yield return WaitUntil(() => !world.IsHeld, 10f);
        Assert.That(world.IsHeld, Is.False, "the freeze wore off");
        SkillBar bar = client.GetComponentsInChildren<SkillBar>(true).Single();
        Transform nine = bar.GetComponentsInChildren<Button>(true).Single(button => button.name == "Slot 9").transform;
        TMP_Text label = nine.GetComponentInChildren<TMP_Text>(true);
        yield return WaitUntil(() => label.text.StartsWith("Frost Lance", StringComparison.Ordinal), 5f);
        label.ForceMeshUpdate();
        Assert.That(bar.ShownSlotWidth, Is.EqualTo(62f), "nine slots narrow");
        Assert.That(label.text, Is.EqualTo("Frost Lance\n9"));
        Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(12f));
        Assert.That(label.textBounds.size.x, Is.LessThanOrEqualTo(label.rectTransform.rect.width + 0.5f));

        yield return Tap(gamepad.dpad.left);
        yield return WaitUntil(() => label.text == "Frost Lance\n[9]", 2f);
        Assert.That(client.IsGamepadOnOwnSkills, Is.True, "D-pad left turned the page");
        SkillResolved? lance = null;
        world.SkillResolvedReceived += resolved =>
            lance = resolved.Caster == world.LocalEntity && resolved.Skill == FrostLance ? resolved : lance;
        float deadline = Time.realtimeSinceStartup + FightTimeoutSeconds;
        while (lance == null && Time.realtimeSinceStartup < deadline)
        {
            if (world.Target == default)
            {
                yield return Tap(keyboard.tabKey);
                yield return WaitUntil(() => world.Target != default, 2f);
            }
            else
            {
                yield return Tap(gamepad.dpad.right);
                yield return WaitUntil(() => lance != null, 3f);
            }
        }

        Assert.That(lance, Is.Not.Null, $"Frost Lance from slot 9: {world.LastRejection} {server.JoinOutput()}");
        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    private IEnumerator Tap(ButtonControl button)
    {
        Press(button);
        yield return null;
        Release(button);
        yield return null;
    }

    private void StoreStatus(string status, int remainingMs)
    {
        m_database!.Execute(
            "INSERT INTO character_status_effects (character_id, status_definition_id, remaining_ms, str_percent, "
            + "agi_percent, vit_percent, int_percent, dex_percent, luk_percent) "
            + $"SELECT id, '{status}', {remainingMs}, 0, 0, 0, 0, 0, 0 FROM characters WHERE name = '{InesName}'");
    }

    private void StoreWorn(string weapon)
    {
        m_database!.Execute(
            "WITH worn AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"version) SELECT id, '{weapon}', 1, 0, 0 FROM characters WHERE name = '{InesName}' "
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

    // The walk's raised chance (Gameplay Systems §9.1), which the server reads from Milestone 19's option section.
    private IEnumerator StartDatabaseAndServer(string? contentPath = null)
    {
        Task<LiveDatabase> starting = LiveDatabase.StartAsync();
        yield return new WaitUntil(() => starting.IsCompleted);
        Assert.That(starting.IsFaulted, Is.False, starting.Exception?.GetBaseException().Message);
        m_database = starting.Result;

        LiveServer server = m_server = new LiveServer();
        server.Start(m_database, "--World:RandomSeed=19 --Combat:AilmentChanceScale=5", contentPath);
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
