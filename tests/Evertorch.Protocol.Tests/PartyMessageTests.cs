using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class PartyMessageTests
{
    private static readonly byte[] InviteBytes =
    {
        0x1B, 0x00,
        0x05, 0x00, 0x42, 0x6F, 0x62, 0x62, 0x79,
        0x07, 0x00, 0x00, 0x00
    };

    private static readonly byte[] ReplyBytes =
    {
        0x1C, 0x00,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61,
        0x01,
        0x08, 0x00, 0x00, 0x00
    };

    private static readonly byte[] LeaveBytes = { 0x1D, 0x00, 0x09, 0x00, 0x00, 0x00 };

    private static readonly byte[] KickBytes =
    {
        0x1E, 0x00,
        0x04, 0x00, 0x43, 0x6F, 0x72, 0x61,
        0x0A, 0x00, 0x00, 0x00
    };

    private static readonly byte[] LeadBytes =
    {
        0x1F, 0x00,
        0x04, 0x00, 0x43, 0x6F, 0x72, 0x61,
        0x0B, 0x00, 0x00, 0x00
    };

    private static readonly byte[] EventBytes = { 0x21, 0x80, 0x04, 0x04, 0x00, 0x43, 0x6F, 0x72, 0x61 };

    // Anna, an Adventurer of level 3 on map.a, leads; Bobby is offline.
    private static readonly byte[] RosterBytes =
    {
        0x22, 0x80,
        0x02,
        0x00,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61,
        0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x61,
        0x03, 0x00,
        0x01,
        0x05, 0x00, 0x6D, 0x61, 0x70, 0x2E, 0x61,
        0x05, 0x00, 0x42, 0x6F, 0x62, 0x62, 0x79,
        0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x62,
        0x0C, 0x00,
        0x00,
        0x00, 0x00
    };

    private static readonly byte[] StatusBytes =
    {
        0x23, 0x80,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61,
        0xEE, 0x02,
        0xE8, 0x03
    };

    public static PartyInvite InviteGolden => new("Bobby", 7);

    public static PartyReply ReplyGolden => new("Anna", true, 8);

    public static PartyLeave LeaveGolden => new(9);

    public static PartyKick KickGolden => new("Cora", 10);

    public static PartyLead LeadGolden => new("Cora", 11);

    public static PartyEvent EventGolden => new(PartyEventKind.Joined, "Cora");

    public static PartyRoster RosterGolden => new(
        0,
        new[]
        {
            new PartyRosterEntry("Anna", new JobDefinitionId("job.a"), 3, new MapDefinitionId("map.a")),
            new PartyRosterEntry("Bobby", new JobDefinitionId("job.b"), 12, null)
        });

    public static PartyMemberStatus StatusGolden => new("Anna", 750, 1000);

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static byte[] Encode(PartyRoster roster)
    {
        return Encode(roster.GetEncodedLength(), bytes => roster.Write(bytes));
    }

    private static byte[] Encode(PartyMemberStatus status)
    {
        return Encode(status.GetEncodedLength(), bytes => status.Write(bytes));
    }

    private static byte[] RosterOf(byte leader, params PartyRosterEntry[] members)
    {
        return Encode(new PartyRoster(leader, members));
    }

    private static PartyRosterEntry Member(string name, string? map = "map.a")
    {
        return new PartyRosterEntry(
            name,
            new JobDefinitionId("job.a"),
            1,
            map == null ? null : new MapDefinitionId(map));
    }

    // The golden roster with Bobby, offline, marked in the world: his map is empty.
    private static byte[] WireMatrixInWorldWithoutAMap()
    {
        byte[] bytes = (byte[])RosterBytes.Clone();
        bytes[bytes.Length - 3] = 0x01;
        return bytes;
    }

    // The golden roster with Anna, in the world, marked offline: her map stays.
    private static byte[] OfflineWithAMap()
    {
        byte[] bytes = (byte[])RosterBytes.Clone();
        bytes[19] = 0x00;
        return bytes;
    }

    [TestCase((byte)0)]
    [TestCase((byte)9)]
    [TestCase((byte)255)]
    public void Event_OfAnUnknownKind_IsMalformed(byte kind)
    {
        byte[] bytes = (byte[])EventBytes.Clone();
        bytes[2] = kind;

        Assert.That(PartyEvent.TryRead(bytes, out _), Is.False);
    }

    [Test]
    public void ClientMessages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(InviteGolden.GetEncodedLength(), bytes => InviteGolden.Write(bytes)),
            Is.EqualTo(InviteBytes));
        Assert.That(Encode(ReplyGolden.GetEncodedLength(), bytes => ReplyGolden.Write(bytes)), Is.EqualTo(ReplyBytes));
        Assert.That(Encode(PartyLeave.EncodedLength, bytes => LeaveGolden.Write(bytes)), Is.EqualTo(LeaveBytes));
        Assert.That(Encode(KickGolden.GetEncodedLength(), bytes => KickGolden.Write(bytes)), Is.EqualTo(KickBytes));
        Assert.That(Encode(LeadGolden.GetEncodedLength(), bytes => LeadGolden.Write(bytes)), Is.EqualTo(LeadBytes));
        Assert.That(PartyInvite.TryRead(InviteBytes, out PartyInvite? invite), Is.True);
        Assert.That((invite!.Name, invite.CommandSequence), Is.EqualTo(("Bobby", 7u)));
        Assert.That(PartyReply.TryRead(ReplyBytes, out PartyReply? reply), Is.True);
        Assert.That((reply!.Inviter, reply.IsAccepted, reply.CommandSequence), Is.EqualTo(("Anna", true, 8u)));
        Assert.That(PartyLeave.TryRead(LeaveBytes, out PartyLeave? leave), Is.True);
        Assert.That(leave!.CommandSequence, Is.EqualTo(9u));
        Assert.That(PartyKick.TryRead(KickBytes, out PartyKick? kick), Is.True);
        Assert.That((kick!.Member, kick.CommandSequence), Is.EqualTo(("Cora", 10u)));
        Assert.That(PartyLead.TryRead(LeadBytes, out PartyLead? lead), Is.True);
        Assert.That((lead!.Member, lead.CommandSequence), Is.EqualTo(("Cora", 11u)));
    }

    [Test]
    public void Commands_NamingAnInvalidName_OrAnAnswerOtherThanZeroOrOne_AreMalformed()
    {
        byte[] reply = (byte[])ReplyBytes.Clone();
        reply[8] = 2;
        var badName = new PartyInvite("Ab cd", 1);

        Assert.That(PartyReply.TryRead(reply, out _), Is.False);
        Assert.That(PartyInvite.TryRead(Encode(badName.GetEncodedLength(), bytes => badName.Write(bytes)), out _),
            Is.False);
        string longest = new('A', CharacterNames.MaxLength);
        Assert.That(new PartyInvite(longest, uint.MaxValue).GetEncodedLength(), Is.EqualTo(31));
        Assert.That(new PartyReply(longest, false, uint.MaxValue).GetEncodedLength(), Is.EqualTo(32));
        Assert.That(new PartyEvent(PartyEventKind.Disbanded, longest).GetEncodedLength(), Is.EqualTo(28));
    }

    [Test]
    public void EveryMessage_CutShortOrTrailedOrMisnamed_IsMalformed()
    {
        WireMatrix.AssertRejectsEveryTruncation(InviteBytes, bytes => PartyInvite.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(ReplyBytes, bytes => PartyReply.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(LeaveBytes, bytes => PartyLeave.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(KickBytes, bytes => PartyKick.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(LeadBytes, bytes => PartyLead.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(EventBytes, bytes => PartyEvent.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(RosterBytes, bytes => PartyRoster.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(StatusBytes, bytes => PartyMemberStatus.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(InviteBytes, bytes => PartyInvite.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(ReplyBytes, bytes => PartyReply.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(LeaveBytes, bytes => PartyLeave.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(KickBytes, bytes => PartyKick.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(LeadBytes, bytes => PartyLead.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(EventBytes, bytes => PartyEvent.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(RosterBytes, bytes => PartyRoster.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(StatusBytes, bytes => PartyMemberStatus.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(InviteBytes, bytes => PartyInvite.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(RosterBytes, bytes => PartyRoster.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(StatusBytes, bytes => PartyMemberStatus.TryRead(bytes, out _));
    }

    // Five members at every limit fit one message and the WebSocket's 1,020 bytes (Network Protocol §7).
    [Test]
    public void Roster_OfFiveAtEveryLimit_IsEightHundredFourBytes()
    {
        PartyRosterEntry[] members = Enumerable.Range(0, PartyRoster.MaxMembers)
            .Select(index => new PartyRosterEntry(
                $"{(char)('A' + index)}{new string('a', CharacterNames.MaxLength - 1)}",
                new JobDefinitionId($"job.{new string('j', DefinitionIdLimits.MaxLength - 4)}"),
                ushort.MaxValue,
                new MapDefinitionId($"map.{new string('m', DefinitionIdLimits.MaxLength - 4)}")))
            .ToArray();

        byte[] bytes = Encode(new PartyRoster(4, members));

        Assert.That(bytes, Has.Length.EqualTo(804));
        Assert.That(PartyRoster.TryRead(bytes, out PartyRoster? read), Is.True);
        Assert.That(read!.Members, Has.Count.EqualTo(5));
    }

    [Test]
    public void Roster_ThatBreaksARule_IsMalformed()
    {
        byte[] sixMembers = (byte[])RosterOf(0, Member("Anna")).Clone();
        sixMembers[2] = 6;

        Assert.That(PartyRoster.TryRead(sixMembers, out _), Is.False, "more than five");
        Assert.That(PartyRoster.TryRead(RosterOf(2, Member("Anna"), Member("Bobby")), out _), Is.False,
            "a leader beyond the members");
        Assert.That(PartyRoster.TryRead(RosterOf(0, Member("Ann")), out _), Is.False, "the name rule");
        Assert.That(PartyRoster.TryRead(WireMatrixInWorldWithoutAMap(), out _), Is.False, "in the world, no map");
        Assert.That(PartyRoster.TryRead(OfflineWithAMap(), out _), Is.False, "offline, a map");
    }

    [Test]
    public void Roster_WithoutAParty_IsNoMembersAndLeaderZero()
    {
        byte[] bytes = RosterOf(0);

        Assert.That(PartyRoster.TryRead(bytes, out PartyRoster? read), Is.True);
        Assert.That(read!.Members, Is.Empty);
        Assert.That(PartyRoster.TryRead(new byte[] { 0x22, 0x80, 0x00, 0x01 }, out _), Is.False, "a leader of none");
    }

    [Test]
    public void ServerMessages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(EventGolden.GetEncodedLength(), bytes => EventGolden.Write(bytes)), Is.EqualTo(EventBytes));
        Assert.That(Encode(RosterGolden), Is.EqualTo(RosterBytes));
        Assert.That(Encode(StatusGolden.GetEncodedLength(), bytes => StatusGolden.Write(bytes)),
            Is.EqualTo(StatusBytes));
        Assert.That(PartyEvent.TryRead(EventBytes, out PartyEvent? partyEvent), Is.True);
        Assert.That((partyEvent!.Kind, partyEvent.Name), Is.EqualTo((PartyEventKind.Joined, "Cora")));
        Assert.That(PartyRoster.TryRead(RosterBytes, out PartyRoster? roster), Is.True);
        Assert.That(roster!.LeaderIndex, Is.Zero);
        Assert.That(
            roster.Members.Select(member => (member.Name, member.Job.Value, member.BaseLevel, member.IsInWorld)),
            Is.EqualTo(new[] { ("Anna", "job.a", (ushort)3, true), ("Bobby", "job.b", (ushort)12, false) }));
        Assert.That(roster.Members[0].Map, Is.EqualTo(new MapDefinitionId("map.a")));
        Assert.That(PartyMemberStatus.TryRead(StatusBytes, out PartyMemberStatus? status), Is.True);
        Assert.That((status!.Name, status.HealthPermille, status.SpiritPermille), Is.EqualTo(("Anna", 750, 1000)));
    }

    [Test]
    public void Status_AboveAThousand_OrOfAnInvalidName_IsMalformed()
    {
        Assert.That(PartyMemberStatus.TryRead(Encode(new PartyMemberStatus("Anna", 1001, 0)), out _), Is.False);
        Assert.That(PartyMemberStatus.TryRead(Encode(new PartyMemberStatus("Anna", 0, 1001)), out _), Is.False);
        Assert.That(PartyMemberStatus.TryRead(Encode(new PartyMemberStatus("Ann", 0, 0)), out _), Is.False);
        Assert.That(
            Encode(new PartyMemberStatus(new string('A', CharacterNames.MaxLength), 0, 0)),
            Has.Length.EqualTo(31),
            "the largest");
    }
}
}
