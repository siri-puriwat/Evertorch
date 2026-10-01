using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The party on the server (Milestone 12 line 6; Gameplay Systems §14; Network Protocol §9, §11): invites with their
///     expiry and cancels, the commands answered after their commit, one change in flight, lost answers settled from
///     the stored membership, and a party held only while a member is in the world.
/// </summary>
[TestFixture]
public sealed class PartyTests
{
    private const string Seven = "Tester7";
    private const string Eight = "Tester8";
    private const string Nine = "Tester9";

    private static (PartyEventKind Kind, string Name)[] Heard(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.PartyEvent)
            .Select(message => PartyEvent.TryRead(message.Payload, out PartyEvent? heard) ? heard! : null!)
            .Select(heard => (heard.Kind, heard.Name))
            .ToArray();
    }

    private static (uint Sequence, CommandRejectionReason Reason)[] Refusals(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return (rejected.CommandSequence, rejected.Reason);
            })
            .ToArray();
    }

    private static long[] MembersOf(TestServer server, long character)
    {
        return server.Parties.TryGetParty(new CharacterId(character), out ServerParty? party)
            ? party!.Members.Select(member => member.CharacterId).ToArray()
            : new long[0];
    }

    private static long LeaderOf(TestServer server, long character)
    {
        server.Parties.TryGetParty(new CharacterId(character), out ServerParty? party);
        return party!.LeaderCharacterId;
    }

    [Test]
    public void ACommitWhoseAnswerIsLost_IsSettledFromTheStoredMembership()
    {
        var rig = new PartyRig(new TestServer(persistence: new PersistenceOptions
            { MaxRetries = 0, RetryBaseDelayMs = 1 }));
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Invite(seven, Eight);
        rig.Server.Store.AmbiguousPartyFailures = 1;

        uint accept = rig.Reply(eight, Seven, true);
        rig.Server.TickUntil(() => MembersOf(rig.Server, 7).Length == 2);

        Assert.That(Refusals(rig.Server, eight).Select(refusal => refusal.Sequence), Does.Not.Contain(accept));
        Assert.That(Heard(rig.Server, seven), Is.EqualTo(new[] { (PartyEventKind.Joined, Eight) }));
        Assert.That(rig.Server.PartyLog.Entries.Select(entry => entry.EventId.Id), Does.Contain(4011));
        Assert.That(rig.Server.Store.PartyChanges, Has.Count.EqualTo(1), "settled by a lookup, not a second commit");
    }

    [Test]
    public void AParty_IsDroppedWhenItsLastMemberLeavesTheWorld_AndLoadedAgainOnEntry()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);
        rig.Server.Disconnect(seven);
        rig.Server.Tick(2);
        Assert.That(rig.Server.Parties.PartyCount, Is.EqualTo(1), "a member is still in the world");

        rig.Server.Disconnect(eight);
        rig.Server.Tick(2);
        Assert.That(rig.Server.Parties.PartyCount, Is.Zero);
        ConnectionId back = rig.Enter(8);
        rig.Server.Tick();

        Assert.That(MembersOf(rig.Server, 8), Is.EqualTo(new[] { 7L, 8L }));
        Assert.That(LeaderOf(rig.Server, 8), Is.EqualTo(7), "an offline leader keeps the lead");
        rig.Leave(back);
        Assert.That(Heard(rig.Server, back).Last(), Is.EqualTo((PartyEventKind.Disbanded, Eight)));
    }

    [Test]
    public void ASecondChange_WhileOneIsInFlight_IsRefusedWith7()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Join(seven, Seven, nine, Nine);
        rig.Server.RunsPersistence = false;

        rig.Kick(seven, Nine);
        uint busy = rig.Leave(eight);
        rig.Server.RunsPersistence = true;
        rig.Server.Tick(2);
        uint later = rig.Leave(eight);

        Assert.That(Refusals(rig.Server, eight), Is.EqualTo(new[] { (busy, CommandRejectionReason.Busy) }));
        Assert.That(later, Is.GreaterThan(busy));
        Assert.That(rig.Server.Parties.PartyCount, Is.Zero, "the kick, then the departure that disbanded it");
    }

    [Test]
    public void Accept_FoundsAPartyLedByTheInviter_AndBothHearTheInviteeJoin()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);

        rig.Invite(seven, Eight);

        Assert.That(Heard(rig.Server, eight), Is.EqualTo(new[] { (PartyEventKind.Invited, Seven) }));
        Assert.That(rig.Server.Parties.PendingInvites, Is.EqualTo(1));
        rig.Reply(eight, "tester7", true);

        Assert.That(MembersOf(rig.Server, 8), Is.EqualTo(new[] { 7L, 8L }));
        Assert.That(LeaderOf(rig.Server, 7), Is.EqualTo(7));
        Assert.That(Heard(rig.Server, seven), Is.EqualTo(new[] { (PartyEventKind.Joined, Eight) }));
        Assert.That(Heard(rig.Server, eight).Last(), Is.EqualTo((PartyEventKind.Joined, Eight)));
        Assert.That(rig.Server.Parties.PendingInvites, Is.Zero);
        Assert.That(rig.Server.Store.LoadPartyAsync(7, default).Result!.Members, Has.Count.EqualTo(2));
    }

    [Test]
    public void Accept_WithoutTheDatabase_IsRefusedWith6_AndTheInviteStays()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Invite(seven, Eight);
        rig.Server.Store.IsUnavailable = true;
        rig.Server.Persistence.Probe();

        uint refused = rig.Reply(eight, Seven, true);

        Assert.That(
            Refusals(rig.Server, eight),
            Is.EqualTo(new[] { (refused, CommandRejectionReason.ServiceUnavailable) }));
        Assert.That(rig.Server.Parties.PendingInvites, Is.EqualTo(1));
    }

    [Test]
    public void AnotherInviteOfTheFounder_CarriesOverToTheNewParty_AndAnInviteesOwnInvitesEnd()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Enter(10);
        rig.Invite(seven, Eight);
        rig.Invite(seven, Nine);
        rig.Invite(eight, "Tester10");

        rig.Reply(eight, Seven, true);
        Assert.That(rig.Server.Parties.PendingInvites, Is.EqualTo(1), "only the founder's invite of Tester9 is left");
        rig.Reply(nine, Seven, true);

        Assert.That(MembersOf(rig.Server, 9), Is.EqualTo(new[] { 7L, 8L, 9L }));
    }

    [Test]
    public void Decline_TellsTheInviter_AndEndsTheInvite()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Invite(seven, Eight);

        rig.Reply(eight, Seven, false);
        uint late = rig.Reply(eight, Seven, true);

        Assert.That(Heard(rig.Server, seven), Is.EqualTo(new[] { (PartyEventKind.Declined, Eight) }));
        Assert.That(Refusals(rig.Server, eight), Is.EqualTo(new[] { (late, CommandRejectionReason.InvalidTarget) }));
        Assert.That(MembersOf(rig.Server, 7), Is.Empty);
    }

    [Test]
    public void Invite_EndsWhenItsInviterLeavesTheWorld()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Invite(seven, Eight);

        rig.Server.Disconnect(seven);
        rig.Server.Tick();
        uint late = rig.Reply(eight, Seven, true);

        Assert.That(rig.Server.Parties.PendingInvites, Is.Zero);
        Assert.That(Refusals(rig.Server, eight), Is.EqualTo(new[] { (late, CommandRejectionReason.InvalidTarget) }));
    }

    [Test]
    public void Invite_IntoAPartyOfFive_IsRefusedWith13()
    {
        var rig = new PartyRig();
        ConnectionId leader = rig.Enter(7);
        for (long character = 8; character <= 11; character++)
        {
            rig.Join(leader, Seven, rig.Enter(character), $"Tester{character}");
        }

        rig.Enter(12);
        uint full = rig.Invite(leader, "Tester12");

        Assert.That(MembersOf(rig.Server, 7), Has.Length.EqualTo(PartyRegistry.MaxMembers));
        Assert.That(Refusals(rig.Server, leader),
            Is.EqualTo(new[] { (full, CommandRejectionReason.RequirementNotMet) }));
    }

    [Test]
    public void Invite_IsRefused_ForNoOneReachable_OneSelf_AnInviteePending_AMemberNotLeading_OrAnInviteeInAParty()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        ConnectionId ten = rig.Enter(10);

        uint nobody = rig.Invite(seven, "Nobody1");
        uint self = rig.Invite(seven, Seven);
        rig.Invite(seven, Eight);
        uint pending = rig.Invite(nine, Eight);
        rig.Reply(eight, Seven, true);
        uint notLeading = rig.Invite(eight, "Tester10");
        uint inParty = rig.Invite(nine, Seven);

        Assert.That(
            Refusals(rig.Server, seven),
            Is.EqualTo(
                new[]
                {
                    (nobody, CommandRejectionReason.InvalidTarget), (self, CommandRejectionReason.NotAllowedNow)
                }));
        Assert.That(
            Refusals(rig.Server, nine),
            Is.EqualTo(
                new[]
                {
                    (pending, CommandRejectionReason.NotAllowedNow), (inParty, CommandRejectionReason.NotAllowedNow)
                }));
        Assert.That(Refusals(rig.Server, eight),
            Is.EqualTo(new[] { (notLeading, CommandRejectionReason.NotAllowedNow) }));
        Assert.That(Heard(rig.Server, ten), Is.Empty);
    }

    [Test]
    public void Invite_ThatWaitsThirtySeconds_Expires_AndTheInviterHearsIt()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Invite(seven, Eight);

        rig.Server.Tick(TestServer.TickRate * PartyRegistry.InviteLifetimeMs / 1000 - 2);
        Assert.That(rig.Server.Parties.PendingInvites, Is.EqualTo(1), "not yet");
        rig.Server.Tick(2);
        uint late = rig.Reply(eight, Seven, true);

        Assert.That(Heard(rig.Server, seven), Is.EqualTo(new[] { (PartyEventKind.Expired, Eight) }));
        Assert.That(Refusals(rig.Server, eight), Is.EqualTo(new[] { (late, CommandRejectionReason.InvalidTarget) }));
    }

    [Test]
    public void Join_OfAThirdMember_IsHeardByEveryMember()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);

        rig.Join(seven, Seven, nine, Nine);

        Assert.That(MembersOf(rig.Server, 9), Is.EqualTo(new[] { 7L, 8L, 9L }));
        foreach (ConnectionId member in new[] { seven, eight, nine })
        {
            Assert.That(Heard(rig.Server, member).Last(), Is.EqualTo((PartyEventKind.Joined, Nine)));
        }
    }

    [Test]
    public void KickAndLead_AreRefused_FromAMemberNotLeading_ForOneself_AndForANameNotInTheParty()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);

        uint memberKick = rig.Kick(eight, Seven);
        uint memberLead = rig.Lead(eight, Eight);
        uint selfKick = rig.Kick(seven, Seven);
        uint stranger = rig.Lead(seven, Nine);

        Assert.That(
            Refusals(rig.Server, eight),
            Is.EqualTo(
                new[]
                {
                    (memberKick, CommandRejectionReason.NotAllowedNow),
                    (memberLead, CommandRejectionReason.NotAllowedNow)
                }));
        Assert.That(
            Refusals(rig.Server, seven),
            Is.EqualTo(
                new[]
                {
                    (selfKick, CommandRejectionReason.NotAllowedNow), (stranger, CommandRejectionReason.InvalidTarget)
                }));
        Assert.That(MembersOf(rig.Server, 7), Is.EqualTo(new[] { 7L, 8L }));
    }

    [Test]
    public void Kick_RemovesAMemberWhoIsOffline_ByName()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Join(seven, Seven, nine, Nine);
        rig.Server.Disconnect(nine);
        rig.Server.Tick(2);

        rig.Kick(seven, "TESTER9");

        Assert.That(MembersOf(rig.Server, 7), Is.EqualTo(new[] { 7L, 8L }));
        Assert.That(Heard(rig.Server, eight).Last(), Is.EqualTo((PartyEventKind.Kicked, Nine)));
        Assert.That(rig.Server.Store.LoadPartyAsync(9, default).Result, Is.Null);
    }

    [Test]
    public void Lead_PassesTheLead_AndEveryMemberHearsIt()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);

        rig.Lead(seven, Eight);

        Assert.That(LeaderOf(rig.Server, 7), Is.EqualTo(8));
        Assert.That(Heard(rig.Server, seven).Last(), Is.EqualTo((PartyEventKind.LeaderChanged, Eight)));
        Assert.That(Heard(rig.Server, eight).Last(), Is.EqualTo((PartyEventKind.LeaderChanged, Eight)));
    }

    [Test]
    public void Leave_OfAPartyOfTwo_DisbandsIt()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);

        rig.Leave(eight);
        uint again = rig.Leave(eight);

        Assert.That(rig.Server.Parties.PartyCount, Is.Zero);
        Assert.That(rig.Server.Store.LoadPartyAsync(7, default).Result, Is.Null);
        Assert.That(Heard(rig.Server, seven).Last(), Is.EqualTo((PartyEventKind.Disbanded, Eight)));
        Assert.That(Heard(rig.Server, eight).Last(), Is.EqualTo((PartyEventKind.Disbanded, Eight)));
        Assert.That(Refusals(rig.Server, eight), Is.EqualTo(new[] { (again, CommandRejectionReason.NotAllowedNow) }));
    }

    [Test]
    public void Leave_OfTheLeader_IsHeardByAll_AndPassesTheLeadToTheEarliestJoined()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Join(seven, Seven, nine, Nine);

        rig.Leave(seven);

        Assert.That(MembersOf(rig.Server, 8), Is.EqualTo(new[] { 8L, 9L }));
        Assert.That(LeaderOf(rig.Server, 9), Is.EqualTo(8));
        Assert.That(MembersOf(rig.Server, 7), Is.Empty);
        Assert.That(Heard(rig.Server, seven).Last(), Is.EqualTo((PartyEventKind.Left, Seven)));
        foreach (ConnectionId member in new[] { eight, nine })
        {
            Assert.That(
                Heard(rig.Server, member).TakeLast(2),
                Is.EqualTo(new[] { (PartyEventKind.Left, Seven), (PartyEventKind.LeaderChanged, Eight) }));
        }
    }

    [Test]
    public void MutualInvites_BothAccepted_FoundOneParty_AndTheSecondAcceptIsRefused()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Invite(seven, Eight);
        rig.Invite(eight, Seven);
        rig.Server.RunsPersistence = false;

        rig.Reply(eight, Seven, true);
        uint second = rig.Reply(seven, Eight, true);
        rig.Server.RunsPersistence = true;
        rig.Server.Tick(2);

        Assert.That(Refusals(rig.Server, seven), Is.EqualTo(new[] { (second, CommandRejectionReason.Busy) }));
        Assert.That(MembersOf(rig.Server, 7), Is.EqualTo(new[] { 7L, 8L }));
        Assert.That(LeaderOf(rig.Server, 8), Is.EqualTo(7));
        Assert.That(rig.Server.Parties.PendingInvites, Is.Zero, "the invite the other way ended with the party");
    }

    [Test]
    public void PartyChanges_AreLoggedByNumberOnly_WithTheirEvents()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Join(seven, Seven, nine, Nine);

        rig.Lead(seven, Eight);
        rig.Kick(eight, Nine);
        rig.Leave(seven);

        Assert.That(
            rig.Server.PartyLog.Entries.Select(entry => entry.EventId.Id),
            Is.EqualTo(new[] { 1017, 1018, 1021, 1020, 1019, 1022 }));
        Assert.That(
            rig.Server.PartyLog.Entries.SelectMany(entry =>
                entry.Fields.Values.Select(value => value?.ToString()).Append(entry.Message)),
            Has.None.Contains("Tester"));
    }

    [Test]
    public void PartyCommands_AreAppliedWhileDead_AndRefusedWhileLoggingOut()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Server.World.TryGetMap(new MapDefinitionId("map.training_ground"), out MapInstance? ground);
        rig.Server.Combat.Kill(ground!, rig.Server.PlayerOf(seven), null, rig.Server.CurrentTick);

        rig.Join(seven, Seven, eight, Eight);
        rig.Server.SendLogout(eight, rig.Next(eight));
        uint leaving = rig.Next(eight);
        rig.Server.SendPartyLeave(eight, leaving);
        rig.Server.Tick();

        Assert.That(rig.Server.PlayerOf(seven).IsDead, Is.True);
        Assert.That(MembersOf(rig.Server, 7), Is.EqualTo(new[] { 7L, 8L }));
        Assert.That(Refusals(rig.Server, eight), Is.EqualTo(new[] { (leaving, CommandRejectionReason.NotAllowedNow) }));
    }

    [Test]
    public void PartyCommands_PastTheirBurst_AreThrottledWith3_AndScored()
    {
        var rig = new PartyRig(new TestServer(abuseOptions: new AbuseOptions()));
        ConnectionId seven = rig.Enter(7);
        int burst = new AbuseOptions().PartyCommandBurst;
        var sent = new List<uint>();

        for (int command = 0; command <= burst; command++)
        {
            uint sequence = rig.Next(seven);
            rig.Server.SendPartyLeave(seven, sequence);
            sent.Add(sequence);
        }

        rig.Server.Tick();

        Assert.That(rig.Server.SessionOf(seven).ThrottledCommands, Is.EqualTo(1));
        Assert.That(Refusals(rig.Server, seven).Last(),
            Is.EqualTo((sent.Last(), CommandRejectionReason.NotAllowedNow)));
    }
}
}
