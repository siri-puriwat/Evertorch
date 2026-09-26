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
        var content = new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster>(),
            new Dictionary<ItemDefinitionId, ClientItem> { { gel.Id, gel }, { sword.Id, sword } },
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
        object loader = typeof(GameClient)
            .GetField("m_contentLoader", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client);
        typeof(StreamingContentLoader).GetProperty(nameof(StreamingContentLoader.Content))!.SetValue(loader, content);
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
    public IEnumerator LoginPanel_WhileDisconnected_ShowsTheConnectFormOnly_AndKeepsTheIdentityInTheClient()
    {
        GameClient client = CreateIdleClient();
        var login = LoginPanel.Create(client);
        m_created.Add(login.gameObject);
        yield return null;

        GameObject[] shown = login.GetComponentsInChildren<Transform>()
            .Select(child => child.gameObject)
            .Where(child => child.name == "Connect" || child.name == "Characters" || child.name == "Reconnect")
            .ToArray();
        TMP_InputField identity = login.GetComponentsInChildren<TMP_InputField>(true)
            .Single(field => field.transform.parent.name == "Identity");
        identity.text = "someone";

        Assert.That(login.IsVisible, Is.True);
        Assert.That(shown.Select(child => child.name), Is.EquivalentTo(new[] { "Connect", "Connect" }),
            "the connect form and its Connect button; no characters and no Reconnect before any close");
        Assert.That(client.Identity, Is.EqualTo("someone"), "the identity lives in the client, in memory");
    }

    [UnityTest]
    public IEnumerator StatusBar_RewritesAPartOnlyWhenItsValueChanges()
    {
        var bar = StatusBar.Create(CreateIdleClient());
        m_created.Add(bar.gameObject);
        yield return null;

        bar.ShowCharacter("Tester", 3);
        bar.ShowHealth(50, 71, false);
        bar.ShowMap("Training Ground");
        bar.ShowPing(42);
        int afterFirst = bar.TextChanges;
        bar.ShowCharacter("Tester", 3);
        bar.ShowHealth(50, 71, false);
        bar.ShowMap("Training Ground");
        bar.ShowPing(42);
        int afterRepeat = bar.TextChanges;
        bar.ShowHealth(0, 71, true);

        Assert.That(afterFirst, Is.EqualTo(4));
        Assert.That(afterRepeat, Is.EqualTo(4), "the same values rewrite nothing");
        Assert.That(bar.TextChanges, Is.EqualTo(5));
        Assert.That(Label(bar, "Name").text, Is.EqualTo("Tester   Lv 3"));
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
        Assert.That(window.Text, Does.EndWith("Training Sword x 1"), "without a connection a press sends nothing");
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
            new InventorySnapshot(4, 0, 1, new[] { new InventoryEntry(1, new ItemDefinitionId(Gel), 3) }));
        yield return null;
        string listed = window.Text;
        int changes = window.TextChanges;
        yield return null;
        int changesLater = window.TextChanges;

        world.Inventory.OnChanged(
            new InventoryChanged(4, 5, new[] { new InventoryEntry(1, new ItemDefinitionId(Gel), 0) }));
        yield return null;

        Assert.That(waiting, Is.EqualTo("Waiting for the server"));
        Assert.That(listed, Is.EqualTo($"{Gel} x 3"), "without content the window names the definition");
        Assert.That(changesLater, Is.EqualTo(changes), "the same revision rewrites nothing");
        Assert.That(window.Text, Is.EqualTo("Empty"));
        Assert.That(window.IsVisible, Is.True);
    }
}
}
