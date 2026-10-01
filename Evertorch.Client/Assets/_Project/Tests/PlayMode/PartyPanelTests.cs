using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
///     The party's panels (Prototype Content §2), built for a client that never starts and entered through a connection
///     that records what it sends: the list with its armed buttons, the invite's question, and Invite in the target
///     frame. The played character is Ann0.
/// </summary>
public sealed class PartyPanelTests
{
    private static readonly JobDefinitionId Adventurer = new("job.adventurer");
    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");
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

    private (GameClient Client, RecordingConnection Recording) Enter()
    {
        var clientObject = new GameObject("TestClient");
        clientObject.SetActive(false);
        m_created.Add(clientObject);
        GameClient client = clientObject.AddComponent<GameClient>();
        NavigationCell[] cells = Enumerable.Repeat(NavigationCell.Level(NavigationSurface.Floor, 0f), 16).ToArray();
        var grid = new NavigationGrid(4, 4, 1f, 0f, 0f, 0.3f, 0.4f, cells);
        var entered = new WorldEntered(
            Ground,
            1,
            Local,
            Adventurer,
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
        return (client, RecordingConnection.EnterWorld(client, entered, grid));
    }

    private static PartyRoster Roster(byte leader, params (string Name, MapDefinitionId? Map)[] members)
    {
        return new PartyRoster(
            leader,
            members.Select(member => new PartyRosterEntry(member.Name, Adventurer, 3, member.Map)).ToArray());
    }

    private T Track<T>(T panel)
        where T : Component
    {
        m_created.Add(panel.gameObject);
        return panel;
    }

    private static bool Click(Component panel, string name)
    {
        Button? button = panel.GetComponentsInChildren<Button>()
            .FirstOrDefault(candidate => candidate.name == name);
        button?.onClick.Invoke();
        return button != null;
    }

    [UnityTest]
    public IEnumerator PartyList_ShowsEachMember_TheLeader_AndWhereTheOthersAre()
    {
        (GameClient client, _) = Enter();
        PartyList list = Track(PartyList.Create(client));
        yield return null;
        bool isShownAlone = list.IsVisible;

        client.Party.Apply(Roster(0, ("Ann0", Ground), ("Bobby", Field), ("Cora", null)));
        yield return null;

        Assert.That(isShownAlone, Is.False, "without a party, nothing");
        Assert.That(list.IsVisible, Is.True);
        Assert.That(
            list.Text.Split('\n'),
            Is.EqualTo(
                new[]
                {
                    "Ann0 (L)  Lv 3 job.adventurer", "Bobby  Lv 3 job.adventurer  map.training_field",
                    "Cora  Lv 3 job.adventurer  Offline"
                }));
        Assert.That(list.Panel!.rect.height,
            Is.EqualTo(PartyList.HeaderHeight + 3 * PartyList.RowHeight).Within(0.01f));
    }

    [UnityTest]
    public IEnumerator Kick_IsArmedByTheFirstPress_AndSentByTheSecond_WhileLeaveIsArmedAlone()
    {
        (GameClient client, RecordingConnection recording) = Enter();
        PartyList list = Track(PartyList.Create(client));
        client.Party.Apply(Roster(0, ("Ann0", Ground), ("Bobby", Ground)));
        yield return null;

        Assert.That(Click(list, PartyList.Kick), Is.True, "the leader sees Kick on Bobby's row");
        yield return null;
        bool isArmed = list.GetComponentsInChildren<TMP_Text>().Any(label => label.text == PartyList.Confirm);
        int sentAfterFirst = recording.SentOf(MessageOpcode.PartyKick).Count();
        list.Press(PartyList.Kick, "Bobby");
        list.Press(PartyList.Leave, string.Empty);

        Assert.That((isArmed, sentAfterFirst), Is.EqualTo((true, 0)), "the first press only arms");
        Assert.That(recording.SentOf(MessageOpcode.PartyKick).Count(), Is.EqualTo(1));
        Assert.That(recording.SentOf(MessageOpcode.PartyLeave), Is.Empty, "Leave is armed, not sent");
    }

    [UnityTest]
    public IEnumerator AMemberNotLeading_SeesNoKickOrLead()
    {
        (GameClient client, _) = Enter();
        PartyList list = Track(PartyList.Create(client));
        client.Party.Apply(Roster(1, ("Ann0", Ground), ("Bobby", Ground)));
        yield return null;

        Assert.That(
            list.GetComponentsInChildren<Button>().Select(button => button.name).Where(name => !name.StartsWith("Row")),
            Is.EqualTo(new[] { "Leave" }));
    }

    [UnityTest]
    public IEnumerator InvitePrompt_AsksTheQuestion_AndAcceptAnswersIt()
    {
        (GameClient client, RecordingConnection recording) = Enter();
        PartyInvitePrompt prompt = Track(PartyInvitePrompt.Create(client));
        client.Party.Invite("Anna", Time.realtimeSinceStartupAsDouble);
        yield return null;
        bool isAsked = prompt.IsVisible;
        string question = prompt.Text;

        Assert.That(Click(prompt, PartyInvitePrompt.Accept), Is.True);
        yield return null;

        Assert.That((isAsked, question), Is.EqualTo((true, "Anna invites you to a party.")));
        Assert.That(prompt.IsVisible, Is.False, "answered");
        byte[] reply = recording.SentOf(MessageOpcode.PartyReply).Single();
        Assert.That(PartyReply.TryRead(reply, out PartyReply? read), Is.True);
        Assert.That((read!.Inviter, read.IsAccepted), Is.EqualTo(("Anna", true)));
    }

    [UnityTest]
    public IEnumerator InvitePrompt_EndsWithTheInvitesThirtySeconds()
    {
        (GameClient client, _) = Enter();
        PartyInvitePrompt prompt = Track(PartyInvitePrompt.Create(client));
        client.Party.Invite("Anna", Time.realtimeSinceStartupAsDouble - ClientParty.InviteSeconds);
        yield return null;

        Assert.That(prompt.IsVisible, Is.False);
        Assert.That(client.Party.Inviter, Is.Null);
    }

    [UnityTest]
    public IEnumerator TargetFrame_OffersInvite_ForAPlayerOutsideTheParty_AndNotForAMember()
    {
        (GameClient client, RecordingConnection recording) = Enter();
        TargetFrame frame = Track(TargetFrame.Create(client));
        ClientWorld world = client.World!;
        world.OnSpawn(
            new EntitySpawn(
                new EntityId(7),
                EntityKind.Player,
                Adventurer.Value,
                new WorldPosition(2.5f, 0f, 2.5f),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                1000,
                string.Empty,
                "Bobby"));
        world.OnTargetChanged(new TargetChanged(Local, new EntityId(7)));
        yield return null;
        yield return null;
        float height = ((RectTransform)frame.GetComponentsInChildren<Image>()[0].transform).rect.height;

        Assert.That(Click(frame, TargetFrame.Invite), Is.True, "Invite shows");
        client.Party.Apply(Roster(0, ("Ann0", Ground), ("Bobby", Ground)));
        yield return null;

        Assert.That(height, Is.LessThanOrEqualTo(TargetFrame.TallestHeight + 0.01f), "within the layout's height");
        byte[] invite = recording.SentOf(MessageOpcode.PartyInvite).Single();
        Assert.That(PartyInvite.TryRead(invite, out PartyInvite? read), Is.True);
        Assert.That(read!.Name, Is.EqualTo("Bobby"));
        Assert.That(frame.GetComponentsInChildren<Button>().Any(button => button.name == TargetFrame.Invite), Is.False);
    }

    [Test]
    public void SlashInviteForOneself_OrANameNoOneHas_IsAnsweredByTheClient()
    {
        (GameClient client, RecordingConnection recording) = Enter();

        uint self = client.InviteToParty("ann0");
        uint nobody = client.InviteToParty("x");

        Assert.That((self, nobody), Is.EqualTo((0u, 0u)));
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Is.EqualTo(new[] { "You cannot invite yourself.", "x is not online." }));
        Assert.That(recording.SentOf(MessageOpcode.PartyInvite), Is.Empty);
    }

    private void SpawnBobby(ClientWorld world)
    {
        world.OnSpawn(
            new EntitySpawn(
                new EntityId(7),
                EntityKind.Player,
                Adventurer.Value,
                new WorldPosition(2.5f, 0f, 2.5f),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                1000,
                string.Empty,
                "Bobby"));
    }

    // A member beside the player shows its HP and SP bars once its status has come; one elsewhere shows the map's
    // name instead (Prototype Content §2).
    [UnityTest]
    public IEnumerator ARow_ShowsItsMembersBars_OnceItsStatusHasCome()
    {
        (GameClient client, _) = Enter();
        PartyList list = Track(PartyList.Create(client));
        client.Party.Apply(Roster(0, ("Ann0", Ground), ("Bobby", Ground), ("Cora", Field)));
        yield return null;
        bool isShownEarly = list.transform.Find("Panel/Row2/Bars").gameObject.activeSelf;

        client.Party.Apply(new PartyMemberStatus("Bobby", 250, 800));
        client.Party.Apply(new PartyMemberStatus("Cora", 500, 500));
        yield return null;

        Assert.That(isShownEarly, Is.False, "no status, no bars");
        Transform bars = list.transform.Find("Panel/Row2/Bars");
        Assert.That(bars.gameObject.activeSelf, Is.True);
        Assert.That(((RectTransform)bars.Find("Health/Fill")).anchorMax.x, Is.EqualTo(0.25f).Within(1e-4f));
        Assert.That(((RectTransform)bars.Find("Spirit/Fill")).anchorMax.x, Is.EqualTo(0.8f).Within(1e-4f));
        Assert.That(list.transform.Find("Panel/Row3/Bars").gameObject.activeSelf, Is.False, "elsewhere, the map");
    }

    // A press on a member's row selects it as a click on its body would; one the client does not see is left alone
    // (Prototype Content §2, §4).
    [UnityTest]
    public IEnumerator ARowsPress_SelectsAMemberTheClientSees()
    {
        (GameClient client, RecordingConnection recording) = Enter();
        PartyList list = Track(PartyList.Create(client));
        SpawnBobby(client.World!);
        client.Party.Apply(Roster(0, ("Ann0", Ground), ("Bobby", Ground), ("Cora", null)));
        yield return null;

        Assert.That(Click(list, "Row3"), Is.True);
        int afterAway = recording.SentOf(MessageOpcode.TargetEntity).Count();
        Assert.That(Click(list, "Row2"), Is.True);

        Assert.That(afterAway, Is.Zero, "Cora is not seen");
        byte[] target = recording.SentOf(MessageOpcode.TargetEntity).Single();
        Assert.That(TargetEntity.TryRead(target, out TargetEntity read), Is.True);
        Assert.That(read.Target, Is.EqualTo(new EntityId(7)));
    }

    // A member's HP shows in the target frame, and over its body (Prototype Content §2).
    [UnityTest]
    public IEnumerator AMembersHealth_ShowsInTheTargetFrame()
    {
        (GameClient client, _) = Enter();
        TargetFrame frame = Track(TargetFrame.Create(client));
        ClientWorld world = client.World!;
        SpawnBobby(world);
        world.OnTargetChanged(new TargetChanged(Local, new EntityId(7)));
        yield return null;
        bool isBarForAStranger = frame.transform.Find("Panel/Bar").gameObject.activeSelf;

        client.Party.Apply(Roster(0, ("Ann0", Ground), ("Bobby", Ground)));
        client.Party.Apply(new PartyMemberStatus("Bobby", 400, 1000));
        yield return null;

        Assert.That(isBarForAStranger, Is.False, "others' HP is not shown");
        Assert.That(frame.transform.Find("Panel/Bar").gameObject.activeSelf, Is.True);
        Assert.That(frame.ShownRatio, Is.EqualTo(0.4f).Within(1e-3f));
    }

    // A throttled party command scores a violation on the server, so the client refuses past its own bucket of four
    // (Network Protocol §11).
    [Test]
    public void PartyCommands_PastTheClientsBucket_AreRefusedByTheClient()
    {
        (GameClient client, RecordingConnection recording) = Enter();

        uint[] sent = Enumerable.Range(0, ChatThrottle.Burst + 1).Select(_ => client.LeaveParty()).ToArray();

        Assert.That(sent.Count(sequence => sequence != 0), Is.EqualTo(ChatThrottle.Burst));
        Assert.That(sent.Last(), Is.Zero);
        Assert.That(recording.SentOf(MessageOpcode.PartyLeave).Count(), Is.EqualTo(ChatThrottle.Burst));
        Assert.That(client.ChatLog.Lines.Last().Text, Is.EqualTo("You are doing that too fast."));
    }
}
}
