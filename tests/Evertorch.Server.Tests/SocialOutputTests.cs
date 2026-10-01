using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Who hears what of Milestone 12 (Network Protocol §9; Milestone 12 verification): a name reaches only those who
///     see the player, a line said nearby only those who know its speaker, a whisper only its pair, and the party's
///     roster, status, events, and lines only its members. Tester7 and Tester8 stand together on the training ground;
///     Tester9 is on the field, out of sight.
/// </summary>
[TestFixture]
public sealed class SocialOutputTests
{
    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");

    private static readonly MessageOpcode[] PartyOpcodes =
    {
        MessageOpcode.PartyEvent, MessageOpcode.PartyMemberStatus
    };

    private static (PartyRig Rig, ConnectionId Seven, ConnectionId Eight, ConnectionId Nine) Three()
    {
        var rig = new PartyRig(new TestServer(withEveryMap: true));
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Server.World.TryGetMap(Ground, out MapInstance? ground);
        rig.Server.PlayerOf(nine).Position = ground!.Definition.Portals.Single().Center;
        rig.Server.TickUntil(() => rig.Server.SessionOf(nine).Character!.Map.Definition.Id == Field);
        rig.Server.Tick();
        rig.Server.Transport.ClearSent();
        return (rig, seven, eight, nine);
    }

    private static string[] SpawnedNames(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.EntitySpawn)
            .Select(message => EntitySpawn.TryRead(message.Payload, out EntitySpawn? spawn) ? spawn!.Name : null!)
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private static (ChatChannel Channel, string Name, string Text)[] Lines(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.ChatReceived)
            .Select(message => ChatReceived.TryRead(message.Payload, out ChatReceived? line) ? line! : null!)
            .Select(line => (line.Channel, line.Name, line.Text))
            .ToArray();
    }

    [Test]
    public void ALineSaidNearby_ReachesOnlyThoseWhoKnowItsSpeaker_AndAWhisperOnlyItsPair()
    {
        (PartyRig rig, ConnectionId seven, ConnectionId eight, ConnectionId nine) = Three();

        rig.Server.SendChat(seven, ChatChannel.Nearby, string.Empty, "near", rig.Next(seven));
        rig.Server.SendChat(eight, ChatChannel.Whisper, "Tester9", "far", rig.Next(eight));
        rig.Server.Tick();

        Assert.That(Lines(rig.Server, seven), Is.EqualTo(new[] { (ChatChannel.Nearby, "Tester7", "near") }));
        Assert.That(
            Lines(rig.Server, eight),
            Is.EqualTo(new[] { (ChatChannel.Nearby, "Tester7", "near"), (ChatChannel.WhisperSent, "Tester9", "far") }));
        Assert.That(Lines(rig.Server, nine), Is.EqualTo(new[] { (ChatChannel.Whisper, "Tester8", "far") }));
    }

    [Test]
    public void AName_ReachesOnlyThoseWhoSeeThePlayer()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId seven = server.EnterWorld(7);
        ConnectionId eight = server.EnterWorld(8);
        server.World.TryGetMap(Ground, out MapInstance? ground);
        server.PlayerOf(eight).Position = ground!.Definition.Portals.Single().Center;
        server.TickUntil(() => server.SessionOf(eight).Character!.Map.Definition.Id == Field);
        server.Transport.ClearSent();

        ConnectionId nine = server.EnterWorld(9);
        server.Tick();

        Assert.That(SpawnedNames(server, seven), Is.EqualTo(new[] { "Tester9" }), "the one who sees it");
        Assert.That(SpawnedNames(server, eight), Is.Empty, "the one on the field");
        Assert.That(SpawnedNames(server, nine), Is.EqualTo(new[] { "Tester7" }));
    }

    [Test]
    public void TheParty_ReachesOnlyItsMembers_WhereverTheyStand()
    {
        (PartyRig rig, ConnectionId seven, ConnectionId eight, ConnectionId nine) = Three();

        rig.Join(seven, "Tester7", nine, "Tester9");
        rig.Server.PlayerOf(nine).CurrentHealth /= 2;
        rig.Server.SendChat(seven, ChatChannel.Party, string.Empty, "members only", rig.Next(seven));
        rig.Server.Tick(TestServer.TickRate + 1);

        Assert.That(rig.Server.Transport.ControlOpcodesSentTo(eight).Intersect(PartyOpcodes), Is.Empty);
        Assert.That(
            rig.Server.Transport.ControlSentTo(eight)
                .Where(message => message.Opcode == MessageOpcode.PartyRoster),
            Is.Empty,
            "no roster for one outside");
        Assert.That(Lines(rig.Server, eight), Is.Empty);
        Assert.That(Lines(rig.Server, nine), Is.EqualTo(new[] { (ChatChannel.Party, "Tester7", "members only") }));
        Assert.That(rig.Server.Transport.ControlOpcodesSentTo(seven), Does.Contain(MessageOpcode.PartyMemberStatus));
    }
}
}
