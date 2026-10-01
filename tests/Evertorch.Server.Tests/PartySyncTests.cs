using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What a party's members hear of it (Milestone 12 line 6; Network Protocol §9): the roster in every baseline and
///     whenever it changes, each other member's health and SP at most once a second, and party chat on any map, while
///     nobody outside the party hears any of it.
/// </summary>
[TestFixture]
public sealed class PartySyncTests
{
    private const string Seven = "Tester7";
    private const string Eight = "Tester8";
    private const string Nine = "Tester9";
    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");

    private static PartyRoster[] Rosters(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.PartyRoster)
            .Select(message => PartyRoster.TryRead(message.Payload, out PartyRoster? roster) ? roster! : null!)
            .ToArray();
    }

    private static (string Name, ushort Health, ushort Spirit)[] Statuses(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.PartyMemberStatus)
            .Select(message =>
                PartyMemberStatus.TryRead(message.Payload, out PartyMemberStatus? status) ? status! : null!)
            .Select(status => (status.Name, status.HealthPermille, status.SpiritPermille))
            .ToArray();
    }

    private static ChatReceived[] Lines(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.ChatReceived)
            .Select(message => ChatReceived.TryRead(message.Payload, out ChatReceived? line) ? line! : null!)
            .ToArray();
    }

    private static string[] NamesIn(PartyRoster roster)
    {
        return roster.Members.Select(member => member.Name).ToArray();
    }

    [Test]
    public void ADeadMember_ShowsNoHealth_AndALivingOneNeverShowsNone()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);
        rig.Server.Tick(TestServer.TickRate);
        PlayerEntity player = rig.Server.PlayerOf(eight);

        player.CurrentHealth = 1;
        rig.Server.Tick(TestServer.TickRate);
        rig.Server.World.TryGetMap(Ground, out MapInstance? ground);
        rig.Server.Combat.Kill(ground!, player, null, rig.Server.CurrentTick);
        rig.Server.Tick();

        ushort[] heard = Statuses(rig.Server, seven).TakeLast(2).Select(status => status.Health).ToArray();
        Assert.That(heard[0], Is.Positive, "one health point still shows");
        Assert.That(heard[1], Is.Zero);
    }

    [Test]
    public void AMemberAway_IsListedAsItLastWas_AndItsReturnIsHeard()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);
        rig.Server.PlayerOf(eight).Level = 6;
        rig.Server.Tick();
        Assert.That(Rosters(rig.Server, seven).Last().Members[1].BaseLevel, Is.EqualTo(6), "a level is heard");

        rig.Server.Disconnect(eight);
        rig.Server.Tick(2);
        PartyRosterEntry away = Rosters(rig.Server, seven).Last().Members[1];
        rig.Enter(8);

        Assert.That((away.Name, away.IsInWorld, away.BaseLevel), Is.EqualTo((Eight, false, (ushort)6)));
        Assert.That(Rosters(rig.Server, seven).Last().Members[1].IsInWorld, Is.True);
    }

    [Test]
    public void AMemberChangingMaps_IsHeardOnTheOthersRoster()
    {
        var rig = new PartyRig(new TestServer(withEveryMap: true));
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);
        rig.Server.World.TryGetMap(Ground, out MapInstance? ground);

        rig.Server.PlayerOf(eight).Position = ground!.Definition.Portals.Single().Center;
        rig.Server.TickUntil(() => rig.Server.SessionOf(eight).Character!.Map.Definition.Id == Field);
        rig.Server.Tick();

        Assert.That(Rosters(rig.Server, seven).Last().Members[1].Map, Is.EqualTo(Field));
        Assert.That(Rosters(rig.Server, eight).Last().Members[1].Map, Is.EqualTo(Field), "its own baseline too");
    }

    [Test]
    public void AMemberWhoLeaves_HearsTheRosterOfNone_AndTheOthersTheirSmallerParty()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Join(seven, Seven, nine, Nine);

        rig.Leave(eight);

        Assert.That(Rosters(rig.Server, eight).Last().Members, Is.Empty);
        Assert.That(NamesIn(Rosters(rig.Server, seven).Last()), Is.EqualTo(new[] { Seven, Nine }));
        Assert.That(NamesIn(Rosters(rig.Server, nine).Last()), Is.EqualTo(new[] { Seven, Nine }));
    }

    [Test]
    public void AMembersHealth_ReachesTheOthers_AtMostOnceASecond()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);
        rig.Server.Tick(TestServer.TickRate);
        PlayerEntity player = rig.Server.PlayerOf(eight);
        rig.Server.Transport.ClearSent();

        int half = player.MaxHealth / 2;
        int quarter = player.MaxHealth / 4;
        player.CurrentHealth = half;
        rig.Server.Tick();
        player.CurrentHealth = quarter;
        rig.Server.Tick(TestServer.TickRate - 2);
        Assert.That(
            Statuses(rig.Server, seven).Select(status => (int)status.Health),
            Is.EqualTo(new[] { half * 1000 / player.MaxHealth }));
        rig.Server.Tick(2);

        Assert.That(
            Statuses(rig.Server, seven).Select(status => (int)status.Health),
            Is.EqualTo(new[] { half * 1000 / player.MaxHealth, quarter * 1000 / player.MaxHealth }));
        Assert.That(Statuses(rig.Server, eight), Is.Empty, "never its own");
    }

    [Test]
    public void Baseline_OfAPlayerWithoutAParty_EndsWithTheRosterOfNone()
    {
        var rig = new PartyRig();

        ConnectionId seven = rig.Enter(7);

        IReadOnlyList<MessageOpcode> sent = rig.Server.Transport.ControlOpcodesSentTo(seven);
        Assert.That(Rosters(rig.Server, seven).Single().Members, Is.Empty);
        Assert.That(
            sent.ToList().IndexOf(MessageOpcode.PartyRoster),
            Is.GreaterThan(sent.ToList().IndexOf(MessageOpcode.QuestLog)));
    }

    [Test]
    public void Forming_SendsEachMemberTheRoster_AndTheOthersHealthAndSp()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Server.PlayerOf(eight).CurrentSpirit = rig.Server.PlayerOf(eight).MaxSpirit / 2;

        rig.Join(seven, Seven, eight, Eight);

        foreach (ConnectionId member in new[] { seven, eight })
        {
            PartyRoster roster = Rosters(rig.Server, member).Last();
            Assert.That(NamesIn(roster), Is.EqualTo(new[] { Seven, Eight }));
            Assert.That(roster.LeaderIndex, Is.Zero);
            Assert.That(roster.Members.Select(entry => entry.Map), Has.All.EqualTo(Ground));
            Assert.That(roster.Members[1].Job, Is.EqualTo(rig.Server.PlayerOf(eight).Job));
        }

        Assert.That(Statuses(rig.Server, seven).Last(), Is.EqualTo((Eight, (ushort)1000, (ushort)500)));
        Assert.That(Statuses(rig.Server, eight).Last(), Is.EqualTo((Seven, (ushort)1000, (ushort)1000)));
    }

    [Test]
    public void PartyChat_ReachesEveryMemberOnAnyMap_AndNoOneElse()
    {
        var rig = new PartyRig(new TestServer(withEveryMap: true));
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Server.World.TryGetMap(Ground, out MapInstance? ground);
        rig.Server.PlayerOf(eight).Position = ground!.Definition.Portals.Single().Center;
        rig.Server.TickUntil(() => rig.Server.SessionOf(eight).Character!.Map.Definition.Id == Field);

        rig.Server.SendChat(seven, ChatChannel.Party, string.Empty, "to the field", rig.Next(seven));
        rig.Server.Tick();

        foreach (ConnectionId member in new[] { seven, eight })
        {
            ChatReceived line = Lines(rig.Server, member).Single();
            Assert.That(
                (line.Channel, line.Speaker, line.Name, line.Text),
                Is.EqualTo((ChatChannel.Party, default(EntityId), Seven, "to the field")));
        }

        Assert.That(Lines(rig.Server, nine), Is.Empty);
    }

    [Test]
    public void TheRosterAndStatuses_NeverReachSomeoneOutsideTheParty()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);

        rig.Server.PlayerOf(eight).CurrentHealth /= 2;
        rig.Server.Tick(TestServer.TickRate);

        Assert.That(Rosters(rig.Server, nine).Select(roster => roster.Members.Count), Has.All.Zero);
        Assert.That(Statuses(rig.Server, nine), Is.Empty);
    }
}
}
