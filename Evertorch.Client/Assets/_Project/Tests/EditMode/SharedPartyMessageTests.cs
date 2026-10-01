using System;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedPartyMessageTests
{
    private static readonly byte[] PartyInviteBytes =
    {
        0x1B, 0x00, 0x05, 0x00, 0x42, 0x6F, 0x62, 0x62, 0x79, 0x07, 0x00, 0x00, 0x00
    };

    private static readonly byte[] PartyReplyBytes =
    {
        0x1C, 0x00, 0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61, 0x01, 0x08, 0x00, 0x00, 0x00
    };

    private static readonly byte[] PartyLeaveBytes = { 0x1D, 0x00, 0x09, 0x00, 0x00, 0x00 };

    private static readonly byte[] PartyKickBytes =
    {
        0x1E, 0x00, 0x04, 0x00, 0x43, 0x6F, 0x72, 0x61, 0x0A, 0x00, 0x00, 0x00
    };

    private static readonly byte[] PartyLeadBytes =
    {
        0x1F, 0x00, 0x04, 0x00, 0x43, 0x6F, 0x72, 0x61, 0x0B, 0x00, 0x00, 0x00
    };

    private static readonly byte[] PartyEventBytes = { 0x21, 0x80, 0x04, 0x04, 0x00, 0x43, 0x6F, 0x72, 0x61 };

    private static readonly byte[] PartyRosterBytes =
    {
        0x22, 0x80, 0x02, 0x00,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61, 0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x61, 0x03, 0x00, 0x01,
        0x05, 0x00, 0x6D, 0x61, 0x70, 0x2E, 0x61,
        0x05, 0x00, 0x42, 0x6F, 0x62, 0x62, 0x79, 0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x62, 0x0C, 0x00, 0x00,
        0x00, 0x00
    };

    private static readonly byte[] PartyMemberStatusBytes =
    {
        0x23, 0x80, 0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61, 0xEE, 0x02, 0xE8, 0x03
    };

    private delegate int Writer(Span<byte> destination);

    private static byte[] Encode(int length, Writer write)
    {
        byte[] bytes = new byte[length];
        write(bytes);
        return bytes;
    }

    [Test]
    public void ClientMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var invite = new PartyInvite("Bobby", 7);
        var reply = new PartyReply("Anna", true, 8);
        var kick = new PartyKick("Cora", 10);
        var lead = new PartyLead("Cora", 11);

        Assert.That(Encode(invite.GetEncodedLength(), invite.Write), Is.EqualTo(PartyInviteBytes));
        Assert.That(Encode(reply.GetEncodedLength(), reply.Write), Is.EqualTo(PartyReplyBytes));
        Assert.That(Encode(PartyLeave.EncodedLength, new PartyLeave(9).Write), Is.EqualTo(PartyLeaveBytes));
        Assert.That(Encode(kick.GetEncodedLength(), kick.Write), Is.EqualTo(PartyKickBytes));
        Assert.That(Encode(lead.GetEncodedLength(), lead.Write), Is.EqualTo(PartyLeadBytes));
        Assert.That(PartyReply.TryRead(PartyReplyBytes, out PartyReply? read), Is.True);
        Assert.That(read!.IsAccepted, Is.True);
        Assert.That(PartyLeave.TryRead(PartyLeaveBytes, out _), Is.True);
        Assert.That(PartyKick.TryRead(PartyKickBytes, out _), Is.True);
        Assert.That(PartyLead.TryRead(PartyLeadBytes, out _), Is.True);
        Assert.That(PartyInvite.TryRead(PartyInviteBytes, out _), Is.True);
    }

    [Test]
    public void ServerMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var partyEvent = new PartyEvent(PartyEventKind.Joined, "Cora");
        var roster = new PartyRoster(
            0,
            new[]
            {
                new PartyRosterEntry("Anna", new JobDefinitionId("job.a"), 3, new MapDefinitionId("map.a")),
                new PartyRosterEntry("Bobby", new JobDefinitionId("job.b"), 12, null)
            });
        var status = new PartyMemberStatus("Anna", 750, 1000);

        Assert.That(Encode(partyEvent.GetEncodedLength(), partyEvent.Write), Is.EqualTo(PartyEventBytes));
        Assert.That(Encode(roster.GetEncodedLength(), roster.Write), Is.EqualTo(PartyRosterBytes));
        Assert.That(Encode(status.GetEncodedLength(), status.Write), Is.EqualTo(PartyMemberStatusBytes));
        Assert.That(PartyEvent.TryRead(PartyEventBytes, out _), Is.True);
        Assert.That(PartyRoster.TryRead(PartyRosterBytes, out PartyRoster? read), Is.True);
        Assert.That(read!.Members[1].IsInWorld, Is.False);
        Assert.That(PartyMemberStatus.TryRead(PartyMemberStatusBytes, out PartyMemberStatus? shown), Is.True);
        Assert.That(shown!.HealthPermille, Is.EqualTo(750));
    }
}
}
