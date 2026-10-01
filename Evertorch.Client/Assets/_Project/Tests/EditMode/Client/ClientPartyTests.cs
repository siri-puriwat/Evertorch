using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client's party (Prototype Content §2): the roster replaces it whole and keeps the statuses of members it
///     still lists, a status updates one member, and an invite waits 30 s for its answer.
/// </summary>
[TestFixture]
public sealed class ClientPartyTests
{
    private static readonly JobDefinitionId Adventurer = new("job.adventurer");
    private static readonly MapDefinitionId Ground = new("map.training_ground");

    private static PartyRoster Roster(byte leader, params string[] names)
    {
        return new PartyRoster(
            leader,
            names.Select(name => new PartyRosterEntry(name, Adventurer, 3, name == "Cora" ? null : Ground)).ToArray());
    }

    [TestCase(PartyEventKind.Invited, "Anna", "Anna invites you to a party.")]
    [TestCase(PartyEventKind.Declined, "Bobby", "Bobby declined your invite.")]
    [TestCase(PartyEventKind.Expired, "Bobby", "Bobby did not answer your invite.")]
    [TestCase(PartyEventKind.Joined, "Bobby", "Bobby joined the party.")]
    [TestCase(PartyEventKind.Joined, "cora", "You joined the party.")]
    [TestCase(PartyEventKind.Left, "Bobby", "Bobby left the party.")]
    [TestCase(PartyEventKind.Left, "Cora", "You left the party.")]
    [TestCase(PartyEventKind.Kicked, "Bobby", "Bobby was removed from the party.")]
    [TestCase(PartyEventKind.Kicked, "Cora", "You were removed from the party.")]
    [TestCase(PartyEventKind.LeaderChanged, "Bobby", "Bobby now leads the party.")]
    [TestCase(PartyEventKind.LeaderChanged, "Cora", "You now lead the party.")]
    [TestCase(PartyEventKind.Disbanded, "Bobby", "The party disbanded.")]
    public void PartyEvents_AreToldInPlainWords(PartyEventKind kind, string name, string words)
    {
        Assert.That(PartyMessages.Describe(kind, name, "Cora"), Is.EqualTo(words));
    }

    [Test]
    public void ARow_NamesTheMemberItsLeadAndLevel_AndSaysWhereItIsWhenNotBesideThePlayer()
    {
        var party = new ClientParty();
        party.Apply(Roster(0, "Anna", "Cora"));

        Assert.That(PartyMessages.Row(party.Members[0], null), Is.EqualTo("Anna (L)  Lv 3 job.adventurer"));
        Assert.That(PartyMessages.Whereabouts(party.Members[0], Ground, null), Is.Empty);
        Assert.That(
            PartyMessages.Whereabouts(party.Members[0], new MapDefinitionId("map.training_field"), null),
            Is.EqualTo("map.training_ground"));
        Assert.That(PartyMessages.Whereabouts(party.Members[1], Ground, null), Is.EqualTo(PartyMessages.Offline));
    }

    [Test]
    public void Clear_ForgetsThePartyAndTheInvite()
    {
        var party = new ClientParty();
        party.Apply(Roster(0, "Anna", "Bobby"));
        party.Invite("Cora", 0.0);

        party.Clear();

        Assert.That((party.IsInParty, party.Inviter), Is.EqualTo((false, (string?)null)));
    }

    [Test]
    public void Invite_WaitsThirtySeconds_ThenEnds()
    {
        var party = new ClientParty();

        party.Invite("Anna", 100.0);
        party.ExpireInvite(129.9);
        Assert.That(party.Inviter, Is.EqualTo("Anna"));
        party.ExpireInvite(130.0);

        Assert.That(party.Inviter, Is.Null);
    }

    [Test]
    public void Roster_ReplacesTheParty_WithItsLeaderAndWhereEachMemberIs()
    {
        var party = new ClientParty();
        int changes = 0;
        party.Changed += () => changes++;

        party.Apply(Roster(1, "Anna", "Bobby", "Cora"));

        Assert.That(party.Members.Select(member => member.Name), Is.EqualTo(new[] { "Anna", "Bobby", "Cora" }));
        Assert.That(party.Leader!.Name, Is.EqualTo("Bobby"));
        Assert.That(party.IsLeader("bobby"), Is.True);
        Assert.That(party.Members.Select(member => member.IsInWorld), Is.EqualTo(new[] { true, true, false }));
        Assert.That(changes, Is.EqualTo(1));
        party.Apply(Roster(0));
        Assert.That(party.IsInParty, Is.False, "the roster of none");
    }

    [Test]
    public void Status_UpdatesItsMember_AndARosterKeepsIt()
    {
        var party = new ClientParty();
        party.Apply(Roster(0, "Anna", "Bobby"));

        party.Apply(new PartyMemberStatus("Bobby", 400, 900));
        party.Apply(new PartyMemberStatus("Nobody1", 1, 1));
        party.Apply(Roster(0, "Anna", "Bobby", "Cora"));

        party.TryGetMember("Bobby", out PartyMember? bobby);
        Assert.That((bobby!.HealthPermille, bobby.SpiritPermille), Is.EqualTo(((int?)400, (int?)900)));
        party.TryGetMember("Cora", out PartyMember? cora);
        Assert.That(cora!.HealthPermille, Is.Null, "no status yet");
    }
}
}
