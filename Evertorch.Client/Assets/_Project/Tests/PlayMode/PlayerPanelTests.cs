using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The panels a player sees (Prototype Content §2), built for a client that never starts, so they show only what
///     they are given: values, or a client world the test feeds by hand. The live tests use them against a server.
/// </summary>
public sealed class PlayerPanelTests
{
    private const string Slime = "monster.training_slime";
    private const string Gel = "item.material.slime_gel";
    private const string Sword = "item.weapon.training_sword";
    private const string Potion = "item.consumable.minor_health";
    private const string Quartermaster = "npc.quartermaster";
    private const string GateWarden = "npc.gate_warden";
    private const string Hunt = "quest.crawler_hunt";
    private const string Crawler = "monster.forest_crawler";

    private static readonly EntityId Local = new(100);

    private readonly List<Object> m_created = new();

    [TearDown]
    public void DestroyCreated()
    {
        foreach (Object created in m_created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        m_created.Clear();
    }

    // Never activated, so the client neither loads content nor connects.
    private GameClient CreateIdleClient()
    {
        var clientObject = new GameObject("TestClient");
        clientObject.SetActive(false);
        m_created.Add(clientObject);
        return clientObject.AddComponent<GameClient>();
    }

    // The world the client would hold after entering, which only the test feeds.
    private static ClientWorld GiveWorld(GameClient client)
    {
        NavigationCell[] cells = Enumerable.Repeat(NavigationCell.Level(NavigationSurface.Floor, 0f), 16).ToArray();
        var entered = new WorldEntered(
            new MapDefinitionId("map.training_ground"),
            1,
            Local,
            new JobDefinitionId("job.adventurer"),
            0,
            new WorldPosition(1.5f, 0f, 1.5f),
            new WorldDirection(0f, 1f),
            5f,
            71,
            71,
            1.5f,
            0,
            new CharacterId(1),
            1,
            0,
            30,
            24,
            24);
        var world = new ClientWorld(new NavigationGrid(4, 4, 1f, 0f, 0f, 0.3f, 0.4f, cells), entered, 20);
        typeof(GameClient)
            .GetField("m_world", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, world);
        return world;
    }

    // The content the client would have loaded, which only the test gives it: a material and a weapon.
    private static void GiveItems(GameClient client)
    {
        var gel = new ClientItem(new ItemDefinitionId(Gel), "Slime Gel", ItemType.Material, "pickup_slime_gel", "gel");
        var sword = new ClientItem(
            new ItemDefinitionId(Sword),
            "Training Sword",
            ItemType.Weapon,
            "pickup_training_sword",
            "sword");
        GiveContent(client, new[] { gel, sword });
    }

    // The shop's content: the items the Quartermaster trades in these tests and the two NPCs.
    private static void GiveShop(GameClient client)
    {
        var npcs = new Dictionary<NpcDefinitionId, ClientNpc>
        {
            [new NpcDefinitionId(Quartermaster)] = new(
                new NpcDefinitionId(Quartermaster),
                "Quartermaster",
                "npc_quartermaster"),
            [new NpcDefinitionId(GateWarden)] = new(new NpcDefinitionId(GateWarden), "Gate Warden", "npc_gate_warden")
        };
        GiveContent(
            client,
            new[]
            {
                new ClientItem(new ItemDefinitionId(Gel), "Slime Gel", ItemType.Material, "pickup_slime_gel", "gel"),
                new ClientItem(
                    new ItemDefinitionId(Sword),
                    "Training Sword",
                    ItemType.Weapon,
                    "pickup_training_sword",
                    "sword"),
                new ClientItem(
                    new ItemDefinitionId(Potion),
                    "Minor Health Potion",
                    ItemType.Consumable,
                    "pickup_minor_health",
                    "potion")
            },
            npcs);
    }

    // The quest's content: the Gate Warden, the crawler it asks for, and the quest's name.
    private static void GiveQuest(GameClient client)
    {
        var warden = new NpcDefinitionId(GateWarden);
        var crawler = new MonsterDefinitionId(Crawler);
        var hunt = new QuestDefinitionId(Hunt);
        GiveContent(
            client,
            new ClientItem[0],
            new Dictionary<NpcDefinitionId, ClientNpc> { [warden] = new(warden, "Gate Warden", "npc_gate_warden") },
            new Dictionary<MonsterDefinitionId, ClientMonster>
            {
                [crawler] = new(crawler, "Forest Crawler", "monster_forest_crawler", "crawler")
            },
            new Dictionary<QuestDefinitionId, ClientQuest> { [hunt] = new(hunt, "Crawler Hunt") });
    }

    private static QuestLog HuntAt(QuestState state, ushort progress)
    {
        return new QuestLog(new[] { new QuestLogEntry(new QuestDefinitionId(Hunt), state, progress, 5) });
    }

    private static string[] ButtonsOf(Component panel)
    {
        return panel.GetComponentsInChildren<Button>().Select(button => button.name).ToArray();
    }

    private static void GiveContent(
        GameClient client,
        IEnumerable<ClientItem> items,
        IReadOnlyDictionary<NpcDefinitionId, ClientNpc>? npcs = null,
        IReadOnlyDictionary<MonsterDefinitionId, ClientMonster>? monsters = null,
        IReadOnlyDictionary<QuestDefinitionId, ClientQuest>? quests = null)
    {
        var content = new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            monsters ?? new Dictionary<MonsterDefinitionId, ClientMonster>(),
            items.ToDictionary(item => item.Id),
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>(),
            npcs,
            quests);
        object loader = typeof(GameClient)
            .GetField("m_contentLoader", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client);
        typeof(StreamingContentLoader).GetProperty(nameof(StreamingContentLoader.Content))!.SetValue(loader, content);
    }

    // The NPC as the client would draw it, so that the window measures its distance to something.
    private void DrawNpc(GameClient client, long entity)
    {
        EntityView view = new GameObject($"Npc {entity}").AddComponent<EntityView>();
        m_created.Add(view.gameObject);
        view.transform.position = new Vector3(2.5f, 0f, 2.5f);
        var views = (Dictionary<EntityId, EntityView>)typeof(GameClient)
            .GetField("m_remoteViews", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client);
        views[new EntityId(entity)] = view;
    }

    // What the Quartermaster trades in these tests, sorted by item ID as the server sends it: the potion and the sword
    // both ways, the gel only to the Quartermaster.
    private static NpcServices QuartermasterServices(long npc)
    {
        return new NpcServices(
            new EntityId(npc),
            new[]
            {
                new NpcServiceEntry(new ItemDefinitionId(Potion), 20, 10),
                new NpcServiceEntry(new ItemDefinitionId(Gel), 0, 2),
                new NpcServiceEntry(new ItemDefinitionId(Sword), 50, 25)
            },
            new NpcQuestOffer[0]);
    }

    // A world with the Quartermaster beside the player and the given rows held with 250 coins, and its window.
    private NpcWindow OpenShop(out ClientWorld world, params InventoryEntry[] rows)
    {
        GameClient client = CreateIdleClient();
        world = GiveWorld(client);
        GiveShop(client);
        Spawn(world, 13, EntityKind.Npc, Quartermaster, 1000);
        DrawNpc(client, 13);
        world.OnNpcServices(QuartermasterServices(13));
        foreach (InventorySnapshot part in InventorySnapshot.CreateParts(4, 250, rows))
        {
            world.Inventory.OnSnapshot(part);
        }

        var window = NpcWindow.Create(client);
        m_created.Add(window.gameObject);
        window.Open(new EntityId(13));
        return window;
    }

    private static void Spawn(ClientWorld world, long entity, EntityKind kind, string definition, ushort healthPermille)
    {
        world.OnSpawn(
            new EntitySpawn(
                new EntityId(entity),
                kind,
                definition,
                new WorldPosition(2.5f, 0f, 2.5f),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                healthPermille));
    }

    private static TMP_Text Label(Component panel, string objectName)
    {
        return panel.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == objectName);
    }

    private static GameObject Slot(SkillBar bar, int slot)
    {
        return bar.GetComponentsInChildren<Button>(true).Single(button => button.name == $"Slot {slot}").gameObject;
    }

    private static string SlotText(SkillBar bar, int slot)
    {
        return Slot(bar, slot).GetComponentInChildren<TMP_Text>(true).text;
    }

    private static SkillList StrikeAndFirstAid(uint strikeCooldownLeftMs)
    {
        return new SkillList(
            new[]
            {
                new SkillListEntry(new SkillDefinitionId("skill.strike"), 1.5f, 8, 2000, 500, strikeCooldownLeftMs),
                new SkillListEntry(new SkillDefinitionId("skill.first_aid"), 0f, 3, 0, 0, 0)
            });
    }

    // A screen-space overlay canvas places its corners in screen pixels.
    private static Rect ScreenRect(Transform target)
    {
        var corners = new Vector3[4];
        ((RectTransform)target).GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    [UnityTest]
    public IEnumerator LoginPanel_WhileDisconnected_ShowsTheConnectFormOnly_AndKeepsTheLoginAndPasswordInTheClient()
    {
        GameClient client = CreateIdleClient();
        var login = LoginPanel.Create(client);
        m_created.Add(login.gameObject);
        yield return null;

        GameObject[] shown = login.GetComponentsInChildren<Transform>()
            .Select(child => child.gameObject)
            .Where(child => child.name == "Connect" || child.name == "Characters" || child.name == "Reconnect")
            .ToArray();
        TMP_InputField[] fields = login.GetComponentsInChildren<TMP_InputField>(true);
        TMP_InputField loginField = fields.Single(field => field.transform.parent.name == "Login");
        TMP_InputField password = fields.Single(field => field.transform.parent.name == "Password");
        TMP_InputField port = fields.Single(field => field.transform.parent.name == "Port");
        loginField.text = "someone";
        password.text = "Secret-Word-1";

        Assert.That(login.IsVisible, Is.True);
        Assert.That(shown.Select(child => child.name), Is.EquivalentTo(new[] { "Connect", "Connect" }),
            "the connect form and its Connect button; no characters and no Reconnect before any close");
        Assert.That(
            fields.Select(field => field.transform.parent.name).Take(4),
            Is.EqualTo(new[] { "Login", "Password", "Host", "Port" }));
        Assert.That(client.Login, Is.EqualTo("someone"), "the login lives in the client, in memory");
        Assert.That(client.Password, Is.EqualTo("Secret-Word-1"), "and so does the password");
        Assert.That(password.contentType, Is.EqualTo(TMP_InputField.ContentType.Password), "the password is masked");
        Assert.That(port.text, Is.EqualTo("7443"), "the gateway's port");
        Assert.That(client.Connection, Is.Null, "nothing connects by itself");
    }

    [UnityTest]
    public IEnumerator StatusBar_RewritesAPartOnlyWhenItsValueChanges()
    {
        var bar = StatusBar.Create(CreateIdleClient());
        m_created.Add(bar.gameObject);
        yield return null;

        bar.ShowCharacter("Tester", 3, "Adventurer", 2);
        bar.ShowHealth(50, 71, false);
        bar.ShowMap("Training Ground");
        bar.ShowPing(42);
        int afterFirst = bar.TextChanges;
        bar.ShowCharacter("Tester", 3, "Adventurer", 2);
        bar.ShowHealth(50, 71, false);
        bar.ShowMap("Training Ground");
        bar.ShowPing(42);
        int afterRepeat = bar.TextChanges;
        bar.ShowHealth(0, 71, true);

        Assert.That(afterFirst, Is.EqualTo(4));
        Assert.That(afterRepeat, Is.EqualTo(4), "the same values rewrite nothing");
        Assert.That(bar.TextChanges, Is.EqualTo(5));
        Assert.That(Label(bar, "Name").text, Is.EqualTo("Tester   Lv 3   Adventurer Lv 2"));
        Assert.That(Label(bar, "Health").text, Is.EqualTo("HP 0 / 71   You died"));
        Assert.That(Label(bar, "Map").text, Is.EqualTo("Training Ground"));
        Assert.That(Label(bar, "Ping").text, Is.EqualTo("42 ms"));
        Assert.That(bar.IsVisible, Is.False, "out of the world the bar stays hidden");
    }

    [UnityTest]
    public IEnumerator StatusBar_ShowsTheWorldsSpAndExperience_FullAtTheCap()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var bar = StatusBar.Create(client);
        m_created.Add(bar.gameObject);
        world.OnCharacterHealth(new CharacterHealth(50, 71, 12, 24));
        world.OnCharacterProgress(new CharacterProgress(1, 15, 30));
        yield return null;

        string spirit = Label(bar, "Spirit").text;
        float half = bar.ShownExperienceRatio;
        world.OnCharacterProgress(new CharacterProgress(10, 0, 0));
        yield return null;

        Assert.That(bar.IsVisible, Is.True);
        Assert.That(spirit, Is.EqualTo("SP 12 / 24"));
        Assert.That(half, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(bar.ShownExperienceRatio, Is.EqualTo(1f), "nothing more to earn at the cap");
    }

    // The job's strip fills under the base experience's from the character sheet (Prototype Content §2); before the
    // sheet it is empty.
    [UnityTest]
    public IEnumerator StatusBar_FillsTheJobStrip_FromTheSheet()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var bar = StatusBar.Create(client);
        m_created.Add(bar.gameObject);
        world.OnCharacterProgress(new CharacterProgress(3, 0, 80));
        yield return null;
        float before = bar.ShownJobExperienceRatio;

        world.OnCharacterSheet(Sheet(2, 10, 40));
        yield return null;
        float quarter = bar.ShownJobExperienceRatio;
        world.OnCharacterSheet(Sheet(10, 0, 0));
        yield return null;

        Assert.That(before, Is.Zero, "no strip before the sheet");
        Assert.That(quarter, Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(bar.ShownJobExperienceRatio, Is.EqualTo(1f), "full at the job's cap");
    }

    private static CharacterSheet Sheet(byte jobLevel, ulong jobExperience, ulong toNext)
    {
        CharacterSheetStat[] stats = Enumerable.Repeat(new CharacterSheetStat(5, 2), 6).ToArray();
        return new CharacterSheet(jobLevel, jobExperience, toNext, 0, 0, stats, 10, 5, 2, 3, 182, 105, 11, 153);
    }

    // What OnChangedMap leaves the panels while the next map loads: no world, and a map change under way.
    private static void BeginMapChange(GameClient client)
    {
        typeof(GameClient)
            .GetField("m_world", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, null);
        typeof(GameClient)
            .GetField("m_isChangingMap", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, true);
    }

    [UnityTest]
    public IEnumerator LoginPanelAndFeedbackLines_ThroughAMapChange_TheLoginStaysHidden_AndTheLinesAreCleared()
    {
        GameClient client = CreateIdleClient();
        GiveWorld(client);
        var login = LoginPanel.Create(client);
        var lines = FeedbackLines.Create(client);
        m_created.Add(login.gameObject);
        m_created.Add(lines.gameObject);
        yield return null;
        lines.Add("Picked up Slime Gel x 1");
        bool wasShown = lines.IsVisible;

        BeginMapChange(client);
        yield return null;

        Assert.That(wasShown, Is.True);
        Assert.That(client.IsInWorld, Is.True);
        Assert.That(login.IsVisible, Is.False, "no login panel while the next map loads");
        Assert.That((lines.Text, lines.IsVisible), Is.EqualTo((string.Empty, false)), "a map change clears the lines");
    }

    [UnityTest]
    public IEnumerator FeedbackLines_SayLevelUp_OnlyWhenTheLevelRises()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var lines = FeedbackLines.Create(client);
        m_created.Add(lines.gameObject);
        yield return null;

        world.OnCharacterProgress(new CharacterProgress(1, 10, 30));
        string afterAward = lines.Text;
        world.OnCharacterProgress(new CharacterProgress(2, 5, 50));

        Assert.That(afterAward, Is.Empty);
        Assert.That(lines.Text, Is.EqualTo("Level up"));
    }

    [UnityTest]
    public IEnumerator FeedbackLines_SayTheJobLevel_OnlyWhenTheSheetRaisesIt()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var lines = FeedbackLines.Create(client);
        m_created.Add(lines.gameObject);
        yield return null;

        world.OnCharacterSheet(Sheet(1, 0, 30));
        string afterBaseline = lines.Text;
        world.OnCharacterSheet(Sheet(1, 20, 30));
        string afterAward = lines.Text;
        world.OnCharacterSheet(Sheet(2, 5, 50));

        Assert.That(afterBaseline, Is.Empty, "the first sheet is the baseline");
        Assert.That(afterAward, Is.Empty);
        Assert.That(lines.Text, Is.EqualTo("Job level 2."));
    }

    [UnityTest]
    public IEnumerator TargetFrame_ShowsOnlyATargetTheServerConfirmed_UntilItIsCleared()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var frame = TargetFrame.Create(client);
        m_created.Add(frame.gameObject);
        Spawn(world, 7, EntityKind.Monster, Slime, 600);
        yield return null;
        bool isShownUnconfirmed = frame.IsVisible;

        world.OnTargetChanged(new TargetChanged(Local, new EntityId(7)));
        yield return null;
        bool isShownConfirmed = frame.IsVisible;
        float ratio = frame.ShownRatio;
        string name = Label(frame, "Name").text;

        world.OnEntityDied(new EntityDied(new EntityId(7), Local, 10));
        yield return null;
        string dead = Label(frame, "Detail").text;
        float deadRatio = frame.ShownRatio;

        world.OnTargetChanged(new TargetChanged(Local, default));
        yield return null;

        Assert.That(isShownUnconfirmed, Is.False, "a monster in view is not a target until the server says so");
        Assert.That(isShownConfirmed, Is.True);
        Assert.That(name, Is.EqualTo(Slime), "without content the frame names the definition");
        Assert.That(ratio, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(dead, Is.EqualTo("Dead"), "no view to measure to, so no distance");
        Assert.That(deadRatio, Is.EqualTo(0f));
        Assert.That(frame.IsVisible, Is.False);
    }

    [UnityTest]
    public IEnumerator TargetFrame_RewritesTextOnlyWhenAShownValueChanges()
    {
        var frame = TargetFrame.Create(CreateIdleClient());
        m_created.Add(frame.gameObject);
        yield return null;

        frame.ShowTarget("Training Slime", 0.5f, 4.23f, false);
        int afterFirst = frame.TextChanges;
        frame.ShowTarget("Training Slime", 0.4f, 4.21f, false);
        int afterSameTenth = frame.TextChanges;
        float ratio = frame.ShownRatio;
        frame.ShowTarget("Training Slime", 0.4f, 3f, false);
        string moved = Label(frame, "Detail").text;
        frame.ShowTarget("Training Slime", 0.4f, 3f, true);

        Assert.That(afterFirst, Is.EqualTo(2), "the name and the detail");
        Assert.That(afterSameTenth, Is.EqualTo(2), "a new HP ratio moves the bar, and 4.21 m still reads 4.2 m");
        Assert.That(ratio, Is.EqualTo(0.4f).Within(0.001f));
        Assert.That(moved, Is.EqualTo("3.0 m"));
        Assert.That(Label(frame, "Detail").text, Is.EqualTo("3.0 m   Dead"));
        Assert.That(frame.ShownRatio, Is.EqualTo(0f));
        Assert.That(frame.TextChanges, Is.EqualTo(4));
    }

    [UnityTest]
    public IEnumerator FeedbackLines_SayWhatWasRefusedAndWhatThePlayerPickedUp_ThenFade()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var lines = FeedbackLines.Create(client);
        m_created.Add(lines.gameObject);
        Spawn(world, 8, EntityKind.ItemDrop, Gel, 1000);
        Spawn(world, 9, EntityKind.ItemDrop, Gel, 1000);
        yield return null;

        world.OnCommandRejected(new CommandRejected(3, CommandRejectionReason.InventoryFull));
        world.OnItemPickedUp(new ItemPickedUp(new EntityId(8), Local, new ItemDefinitionId(Gel), 2));
        world.OnItemPickedUp(new ItemPickedUp(new EntityId(9), new EntityId(55), new ItemDefinitionId(Gel), 1));
        string shown = lines.Text;
        bool isVisible = lines.IsVisible;
        for (int index = 0; index < FeedbackLines.MaxLines; index++)
        {
            world.OnCommandRejected(new CommandRejected(4, CommandRejectionReason.OutOfRange));
        }

        string full = lines.Text;
        yield return new WaitForSecondsRealtime(FeedbackLines.LineSeconds + 0.2f);

        Assert.That(shown, Is.EqualTo($"Your inventory is full.\nPicked up {Gel} x 2"),
            "another player's pickup is not this player's news");
        Assert.That(isVisible, Is.True);
        Assert.That(full.Split('\n'), Is.EqualTo(Enumerable.Repeat("That is too far away.", FeedbackLines.MaxLines)));
        Assert.That(lines.Text, Is.Empty);
        Assert.That(lines.IsVisible, Is.False);
    }

    [UnityTest]
    public IEnumerator StatusBar_ShowsEachStatusEffect_WithItsWholeSecondsLeft()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var bar = StatusBar.Create(client);
        m_created.Add(bar.gameObject);
        yield return null;
        string withoutEffects = Label(bar, "Effects").text;

        world.OnStatusEffects(
            new StatusEffects(new[] { new StatusEffectEntry(new StatusDefinitionId("status.focus"), 59_200) }));
        yield return null;
        string started = Label(bar, "Effects").text;
        int changes = bar.TextChanges;
        yield return null;
        int changesLater = bar.TextChanges;
        world.Advance(20f);
        yield return null;
        string later = Label(bar, "Effects").text;
        world.OnStatusEffects(new StatusEffects(new StatusEffectEntry[0]));
        yield return null;

        Assert.That(withoutEffects, Is.Empty);
        Assert.That(started, Is.EqualTo("status.focus 60s"), "without content the bar names the definition");
        Assert.That(changesLater, Is.EqualTo(changes), "an unchanged second rewrites nothing");
        Assert.That(later, Is.EqualTo("status.focus 40s"));
        Assert.That(Label(bar, "Effects").text, Is.Empty);
    }

    [UnityTest]
    public IEnumerator SkillBar_ShowsTheListedSlots_WithTheirKeyOrWhatIsLeftOfTheCooldown()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var bar = SkillBar.Create(client);
        m_created.Add(bar.gameObject);
        yield return null;
        bool isShownWithoutAList = bar.IsVisible;

        world.OnSkillList(StrikeAndFirstAid(1500));
        yield return null;
        string strike = SlotText(bar, 1);
        string firstAid = SlotText(bar, 2);
        bool isFocusShown = Slot(bar, 3).activeSelf;
        int changes = bar.TextChanges;
        yield return null;
        int changesLater = bar.TextChanges;
        world.Advance(1.5f);
        yield return null;

        Assert.That(isShownWithoutAList, Is.False);
        Assert.That(strike, Is.EqualTo("skill.strike\n1.5 s"), "without content the bar names the definition");
        Assert.That(firstAid, Is.EqualTo("skill.first_aid\n2"), "a skill ready to use shows its key");
        Assert.That(isFocusShown, Is.False, "Focus waits until the server lists it");
        Assert.That(changesLater, Is.EqualTo(changes), "an unchanged tenth of a second rewrites nothing");
        Assert.That(SlotText(bar, 1), Is.EqualTo("skill.strike\n1"));
        Assert.That(bar.IsVisible, Is.True);
        Assert.That(Slot(bar, 1).GetComponent<Button>().navigation.mode, Is.EqualTo(Navigation.Mode.None));
    }

    [UnityTest]
    public IEnumerator SkillBar_ShowsAPotionSlot_WhileTheInventoryHoldsItsPotion_WithHowMany()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var bar = SkillBar.Create(client);
        m_created.Add(bar.gameObject);
        yield return null;
        bool isShownEmpty = Slot(bar, 4).activeSelf;

        world.Inventory.OnSnapshot(
            new InventorySnapshot(
                4,
                0,
                0,
                1,
                new[] { new InventoryEntry(7, new ItemDefinitionId("item.consumable.minor_health"), 3) }));
        yield return null;

        Assert.That(isShownEmpty, Is.False);
        Assert.That(Slot(bar, 4).activeSelf, Is.True);
        Assert.That(
            SlotText(bar, 4),
            Is.EqualTo("item.consumable.minor_health x 3\n4"),
            "without content the bar names the definition");
        Assert.That(Slot(bar, 5).activeSelf, Is.False, "no mana potion");
        Assert.That(bar.IsVisible, Is.True, "a potion alone shows the bar");
    }

    [UnityTest]
    public IEnumerator SkillBarAndFeedbackLines_AtThisScreenSize_ClearEachOtherTheStickAndTheTouchButtons()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var touch = TouchControls.Create();
        m_created.Add(touch.gameObject);
        touch.SetVisible(true);
        var bar = SkillBar.Create(client);
        m_created.Add(bar.gameObject);
        var lines = FeedbackLines.Create(client);
        m_created.Add(lines.gameObject);
        yield return null;

        world.OnSkillList(StrikeAndFirstAid(0));
        for (int index = 0; index < FeedbackLines.MaxLines; index++)
        {
            world.OnCommandRejected(new CommandRejected(3, CommandRejectionReason.OutOfRange));
        }

        yield return null;
        yield return null;
        yield return null;
        Rect shownBar = ScreenRect(bar.transform.Find("Bar"));
        Rect shownLines = ScreenRect(lines.transform.Find("Panel"));
        var covered = new List<Rect> { ScreenRect(touch.StickArea!) };
        covered.AddRange(
            new[] { "Next", "Previous", "Clear" }.Select(name => ScreenRect(
                touch.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == name))));
        float unitsToPixels = Screen.width / ClientUI.CanvasWidth;
        Debug.Log($"Panels at {Screen.width} x {Screen.height}: bar {shownBar}, lines {shownLines}");

        Assert.That(lines.IsVisible, Is.True);
        Assert.That(shownLines.yMin - shownBar.yMax, Is.GreaterThanOrEqualTo(16f * unitsToPixels - 0.5f));
        Assert.That(shownLines.yMin, Is.GreaterThanOrEqualTo(0.2f * Screen.height - 0.5f));
        Assert.That(covered.Where(rect => rect.Overlaps(shownBar)), Is.Empty, "the bar is clear of the touch controls");
        Assert.That(covered.Where(rect => rect.Overlaps(shownLines)), Is.Empty, "the lines are too");
    }

    [UnityTest]
    public IEnumerator InventoryWindow_ShowsARowWithAnActionAsAButton_AndMarksTheWornOne()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveItems(client);
        var window = InventoryWindow.Create(client);
        m_created.Add(window.gameObject);

        world.Inventory.OnSnapshot(
            new InventorySnapshot(
                4,
                0,
                0,
                1,
                new[]
                {
                    new InventoryEntry(1, new ItemDefinitionId(Gel), 3),
                    new InventoryEntry(2, new ItemDefinitionId(Sword), 1, EquipmentSlot.Weapon),
                    new InventoryEntry(3, new ItemDefinitionId(Sword), 1)
                }));
        yield return null;
        Button[] buttons = window.GetComponentsInChildren<Button>(true);

        Assert.That(window.Text, Is.EqualTo("Slime Gel x 3\nTraining Sword x 1 (equipped)\nTraining Sword x 1"));
        Assert.That(
            buttons.Select(button => button.name),
            Is.EqualTo(new[] { "Training Sword x 1 (equipped)", "Training Sword x 1" }),
            "the gel has no action, so its row is text");
        Assert.That(buttons.Select(button => button.navigation.mode), Is.All.EqualTo(Navigation.Mode.None));
        buttons[1].onClick.Invoke();
        yield return null;
        Assert.That(window.Text, Does.EndWith("Training Sword x 1"), "a press shows nothing until the server answers");
    }

    // Wrapped onto a second line, the worn sword's row spilled out of its row, and the list's mask cut off its top.
    [UnityTest]
    public IEnumerator InventoryWindow_ShowsALongRowOnOneLine_InsideItsRow()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveItems(client);
        var window = InventoryWindow.Create(client);
        m_created.Add(window.gameObject);

        world.Inventory.OnSnapshot(
            new InventorySnapshot(
                4,
                0,
                0,
                1,
                new[] { new InventoryEntry(2, new ItemDefinitionId(Sword), 1, EquipmentSlot.Weapon) }));
        yield return null;
        yield return null;
        TMP_Text label = window.GetComponentsInChildren<Button>(true).Single().GetComponentInChildren<TMP_Text>();
        label.ForceMeshUpdate();

        Assert.That(label.text, Is.EqualTo("Training Sword x 1 (equipped)"));
        Assert.That(label.textInfo.lineCount, Is.EqualTo(1), "a long row stays on one line");
        Assert.That(
            label.textBounds.size.y,
            Is.LessThanOrEqualTo(((RectTransform)label.transform).rect.height + 0.5f),
            "inside its row");
    }

    // A row under a touch button would take the taps meant for it, and a press of a row equips or drinks.
    [UnityTest]
    public IEnumerator InventoryWindow_WithTheTouchControlsShown_StopsAboveTheirButtons_AndKeepsEveryRow()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveItems(client);
        var touch = TouchControls.Create();
        m_created.Add(touch.gameObject);
        touch.SetVisible(true);
        typeof(GameClient).GetProperty(nameof(GameClient.Touch))!.SetValue(client, touch);
        var window = InventoryWindow.Create(client);
        m_created.Add(window.gameObject);

        world.Inventory.OnSnapshot(
            new InventorySnapshot(
                4,
                0,
                0,
                1,
                Enumerable.Range(1, InventorySnapshot.MaxEntries)
                    .Select(row => new InventoryEntry(row, new ItemDefinitionId(Sword), 1))
                    .ToArray()));
        yield return null;
        yield return null;
        yield return null;
        Rect shown = ScreenRect(window.transform.Find("Panel"));
        Rect[] buttons = new[] { "Next", "Previous", "Clear" }
            .Select(name => ScreenRect(
                touch.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == name)))
            .ToArray();
        Debug.Log($"Inventory at {Screen.width} x {Screen.height}: {shown}");

        Assert.That(buttons.Where(rect => rect.Overlaps(shown)), Is.Empty, "no row lies under a touch button");
        Assert.That(
            window.GetComponentsInChildren<Button>(true),
            Has.Length.EqualTo(InventorySnapshot.MaxEntries),
            "the rows that do not fit scroll");
    }

    [UnityTest]
    public IEnumerator InventoryWindow_ShowsTheCoinsAboveTheRows_OnceCurrent_AndAfterEachChange()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveItems(client);
        var window = InventoryWindow.Create(client);
        m_created.Add(window.gameObject);
        yield return null;
        string waiting = window.CoinsText;

        world.Inventory.OnSnapshot(
            new InventorySnapshot(4, 250, 0, 1, new[] { new InventoryEntry(1, new ItemDefinitionId(Gel), 3) }));
        yield return null;
        yield return null;
        string shown = window.CoinsText;
        Rect coins =
            ScreenRect(window.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == "Coins"));
        Rect list = ScreenRect(window.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == "List"));
        world.Inventory.OnChanged(new InventoryChanged(4, 5, 70, new InventoryEntry[0]));
        yield return null;

        Assert.That(waiting, Is.Empty, "no coins until the inventory is current");
        Assert.That(shown, Is.EqualTo("Coins: 250"));
        Assert.That(coins.yMin, Is.GreaterThanOrEqualTo(list.yMax - 0.5f), "on the heading's line, above the rows");
        Assert.That(window.CoinsText, Is.EqualTo("Coins: 70"));
        Assert.That(window.Text, Is.EqualTo("Slime Gel x 3"), "a change of coins alone leaves the rows");
    }

    [UnityTest]
    public IEnumerator InventoryWindow_ListsTheRows_AndRewritesOnlyForANewRevision()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        var window = InventoryWindow.Create(client);
        m_created.Add(window.gameObject);
        yield return null;
        string waiting = window.Text;

        world.Inventory.OnSnapshot(
            new InventorySnapshot(4, 0, 0, 1, new[] { new InventoryEntry(1, new ItemDefinitionId(Gel), 3) }));
        yield return null;
        string listed = window.Text;
        int changes = window.TextChanges;
        yield return null;
        int changesLater = window.TextChanges;

        world.Inventory.OnChanged(
            new InventoryChanged(4, 5, 0, new[] { new InventoryEntry(1, new ItemDefinitionId(Gel), 0) }));
        yield return null;

        Assert.That(waiting, Is.EqualTo("Waiting for the server"));
        Assert.That(listed, Is.EqualTo($"{Gel} x 3"), "without content the window names the definition");
        Assert.That(changesLater, Is.EqualTo(changes), "the same revision rewrites nothing");
        Assert.That(window.Text, Is.EqualTo("Empty"));
        Assert.That(window.IsVisible, Is.True);
    }

    // The Quartermaster's part (Prototype Content §2): the coins, what it sells at its price, and the rows it buys at
    // what one fetches. A worn row and a row of an item it does not buy are left out, and only a stack offers All.
    [UnityTest]
    public IEnumerator NpcWindow_ForTheQuartermaster_ListsWhatItSellsAndTheRowsItBuys_WithTheCoins()
    {
        NpcWindow window = OpenShop(
            out ClientWorld _,
            new InventoryEntry(1, new ItemDefinitionId(Gel), 12),
            new InventoryEntry(2, new ItemDefinitionId(Sword), 1, EquipmentSlot.Weapon),
            new InventoryEntry(3, new ItemDefinitionId(Sword), 1),
            new InventoryEntry(4, new ItemDefinitionId("item.material.crawler_shell"), 3));
        yield return null;
        Button[] buttons = window.GetComponentsInChildren<Button>(true);

        Assert.That(window.IsOpen, Is.True);
        Assert.That((window.ShownName, window.CoinsText), Is.EqualTo(("Quartermaster", "Coins: 250")));
        Assert.That(
            window.Text.Split('\n'),
            Is.EqualTo(
                new[]
                {
                    "Buy", "Minor Health Potion: 20 coins", "Training Sword: 50 coins", "Sell",
                    "Slime Gel x 12: 2 coins each", "Training Sword x 1: 25 coins"
                }));
        Assert.That(
            buttons.Select(button => button.name),
            Is.EqualTo(
                new[]
                {
                    "Minor Health Potion: 20 coins", "Training Sword: 50 coins", "Slime Gel x 12: 2 coins each",
                    "All: 24 coins", "Training Sword x 1: 25 coins", "Close"
                }));
        Assert.That(buttons.Select(button => button.navigation.mode), Is.All.EqualTo(Navigation.Mode.None));
        buttons[0].onClick.Invoke();
        buttons[3].onClick.Invoke();
        yield return null;
        Assert.That(window.Text, Does.Contain("Slime Gel x 12"), "a press shows nothing until the server answers");
    }

    [UnityTest]
    public IEnumerator NpcWindow_ForAnNpcThatDoesNotTrade_ShowsItsNameAndCloseOnly()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveShop(client);
        Spawn(world, 14, EntityKind.Npc, GateWarden, 1000);
        DrawNpc(client, 14);
        world.OnNpcServices(new NpcServices(new EntityId(14), new NpcServiceEntry[0], new NpcQuestOffer[0]));
        world.Inventory.OnSnapshot(new InventorySnapshot(4, 250, 0, 1, new InventoryEntry[0]));
        var window = NpcWindow.Create(client);
        m_created.Add(window.gameObject);

        window.Open(new EntityId(14));
        yield return null;

        Assert.That(window.IsOpen, Is.True);
        Assert.That((window.ShownName, window.CoinsText, window.Text), Is.EqualTo(("Gate Warden", "", "")));
        Assert.That(
            window.GetComponentsInChildren<Button>().Select(button => button.name),
            Is.EqualTo(new[] { "Close" }));
    }

    // Only a committed change moves the lists and the coins: a sold row leaves the Sell list.
    [UnityTest]
    public IEnumerator NpcWindow_RewritesItsListsOnlyForANewRevision()
    {
        NpcWindow window = OpenShop(
            out ClientWorld world,
            new InventoryEntry(1, new ItemDefinitionId(Gel), 12),
            new InventoryEntry(3, new ItemDefinitionId(Sword), 1));
        yield return null;
        int changes = window.TextChanges;
        yield return null;
        int changesLater = window.TextChanges;

        world.Inventory.OnChanged(
            new InventoryChanged(4, 5, 274, new[] { new InventoryEntry(1, new ItemDefinitionId(Gel), 0) }));
        yield return null;
        string afterTheGel = window.Text;
        string coins = window.CoinsText;
        world.Inventory.OnChanged(
            new InventoryChanged(5, 6, 299, new[] { new InventoryEntry(3, new ItemDefinitionId(Sword), 0) }));
        yield return null;

        Assert.That(changesLater, Is.EqualTo(changes), "the same revision rewrites nothing");
        Assert.That(afterTheGel, Does.EndWith("Sell\nTraining Sword x 1: 25 coins"));
        Assert.That(coins, Is.EqualTo("Coins: 274"));
        Assert.That(window.Text, Does.EndWith("Sell\nNothing to sell"));
        Assert.That(window.CoinsText, Is.EqualTo("Coins: 299"));
    }

    // A row over a skill slot would take the presses meant for it, and a press of a row buys or sells.
    [UnityTest]
    public IEnumerator NpcWindow_WithMoreRowsThanFit_StopsAboveTheSkillBar_AndKeepsEveryRow()
    {
        NpcWindow window = OpenShop(
            out ClientWorld _,
            Enumerable.Range(1, 100).Select(row => new InventoryEntry(row, new ItemDefinitionId(Gel), 1)).ToArray());
        yield return null;
        yield return null;
        yield return null;
        Rect shown = ScreenRect(window.transform.Find("Panel"));
        float unitsToPixels = Screen.width / ClientUI.CanvasWidth;
        Debug.Log($"NPC window at {Screen.width} x {Screen.height}: {shown}");

        Assert.That(shown.yMin, Is.GreaterThanOrEqualTo((SkillBar.Top + 8f) * unitsToPixels - 0.5f), "above the bar");
        Assert.That(
            window.GetComponentsInChildren<Button>(true),
            Has.Length.EqualTo(2 + 100 + 1),
            "the rows that do not fit scroll");
    }

    // Purchases and sales come from each committed change's coins and rows (Prototype Content §2).
    [UnityTest]
    public IEnumerator FeedbackLines_SayWhatWasBoughtAndSold_FromTheCommittedChanges()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveShop(client);
        var lines = FeedbackLines.Create(client);
        m_created.Add(lines.gameObject);
        world.Inventory.OnSnapshot(
            new InventorySnapshot(4, 100, 0, 1, new[] { new InventoryEntry(1, new ItemDefinitionId(Gel), 12) }));
        yield return null;

        world.Inventory.OnChanged(
            new InventoryChanged(4, 5, 124, new[] { new InventoryEntry(1, new ItemDefinitionId(Gel), 0) }));
        world.Inventory.OnChanged(
            new InventoryChanged(5, 6, 74, new[] { new InventoryEntry(2, new ItemDefinitionId(Sword), 1) }));
        world.Inventory.OnChanged(
            new InventoryChanged(
                6,
                7,
                74,
                new[] { new InventoryEntry(2, new ItemDefinitionId(Sword), 1, EquipmentSlot.Weapon) }));

        Assert.That(
            lines.Text,
            Is.EqualTo("Sold Slime Gel x 12 for 24 coins.\nBought Training Sword for 50 coins."),
            "putting the sword on says nothing");
    }

    // The Gate Warden's part (Prototype Content §2): the quest, its objective, and its reward, then Accept while the
    // character has no entry, the progress while it runs, Turn in once it reaches its count, and Completed at the end.
    [UnityTest]
    public IEnumerator NpcWindow_ForTheGateWarden_OffersItsQuest_ThenShowsWhereTheCharacterStandsWithIt()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveQuest(client);
        Spawn(world, 14, EntityKind.Npc, GateWarden, 1000);
        DrawNpc(client, 14);
        world.OnNpcServices(
            new NpcServices(
                new EntityId(14),
                new NpcServiceEntry[0],
                new[]
                {
                    new NpcQuestOffer(new QuestDefinitionId(Hunt), new MonsterDefinitionId(Crawler), 5, 150, 0, 100)
                }));
        world.OnQuestLog(new QuestLog(new QuestLogEntry[0]));
        var window = NpcWindow.Create(client);
        m_created.Add(window.gameObject);

        window.Open(new EntityId(14));
        yield return null;
        string offered = window.Text;
        string[] offeredButtons = ButtonsOf(window);
        world.OnQuestLog(HuntAt(QuestState.Active, 3));
        yield return null;
        string active = window.Text;
        string[] activeButtons = ButtonsOf(window);
        world.OnQuestLog(HuntAt(QuestState.Active, 5));
        yield return null;
        string ready = window.Text;
        string[] readyButtons = ButtonsOf(window);
        world.OnQuestLog(HuntAt(QuestState.Completed, 5));
        yield return null;

        Assert.That(
            offered.Split('\n'),
            Is.EqualTo(
                new[]
                {
                    "Crawler Hunt", "Defeat: Forest Crawler × 5", "Reward: 150 base experience, 100 coins", "Accept"
                }));
        Assert.That(offeredButtons, Is.EqualTo(new[] { "Accept", "Close" }));
        Assert.That(active, Does.EndWith("\nProgress: 3/5"));
        Assert.That(activeButtons, Is.EqualTo(new[] { "Close" }), "no turn-in before the count");
        Assert.That(ready, Does.EndWith("\nProgress: 5/5\nTurn in"));
        Assert.That(readyButtons, Is.EqualTo(new[] { "Turn in", "Close" }));
        Assert.That(window.Text, Does.EndWith("\nCompleted"));
        Assert.That(ButtonsOf(window), Is.EqualTo(new[] { "Close" }));
        Assert.That(window.CoinsText, Is.Empty, "the Gate Warden keeps no shop");
    }

    [UnityTest]
    public IEnumerator StatusBar_ShowsAnActiveQuestsProgress_ReadyAtItsCount_AndNothingOnceCompleted()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveQuest(client);
        var bar = StatusBar.Create(client);
        m_created.Add(bar.gameObject);
        yield return null;
        string none = bar.QuestText;

        world.OnQuestLog(HuntAt(QuestState.Active, 3));
        yield return null;
        string active = bar.QuestText;
        world.OnQuestLog(HuntAt(QuestState.Active, 5));
        yield return null;
        string ready = bar.QuestText;
        world.OnQuestLog(HuntAt(QuestState.Completed, 5));
        yield return null;

        Assert.That(none, Is.Empty);
        Assert.That(active, Is.EqualTo("Crawler Hunt 3/5"), "no connection saw an offer, so the quest names itself");
        Assert.That(ready, Is.EqualTo("Crawler Hunt 5/5 (ready)"));
        Assert.That(bar.QuestText, Is.Empty);
    }

    // The first quest log of a world is its baseline, not news; each later one says what changed (Prototype Content §2).
    [UnityTest]
    public IEnumerator FeedbackLines_SayWhatEachQuestLogChanged_ButNotTheBaseline()
    {
        GameClient client = CreateIdleClient();
        ClientWorld world = GiveWorld(client);
        GiveQuest(client);
        var lines = FeedbackLines.Create(client);
        m_created.Add(lines.gameObject);
        yield return null;

        world.OnQuestLog(HuntAt(QuestState.Active, 1));
        string afterTheBaseline = lines.Text;
        world.OnQuestLog(HuntAt(QuestState.Active, 2));
        world.OnQuestLog(HuntAt(QuestState.Active, 5));
        world.OnQuestLog(HuntAt(QuestState.Completed, 5));

        Assert.That(afterTheBaseline, Is.Empty, "a quest held since an earlier session is no news");
        Assert.That(
            lines.Text.Split('\n'),
            Is.EqualTo(
                new[] { "Crawler Hunt: 2/5.", "Crawler Hunt is ready to turn in.", "Completed Crawler Hunt." }));
    }
}
}
