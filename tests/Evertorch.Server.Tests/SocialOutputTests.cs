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
///     Tester9 is on the field, out of sight. Milestone 14 adds a trade's request, sides, and end only to its two
///     traders, beside a third player who hears none of it, and storage only to the character at the Storekeeper, not
///     to another character of its account in the world.
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

    private static readonly MessageOpcode[] TradeOpcodes =
    {
        MessageOpcode.TradeEvent, MessageOpcode.TradeSide, MessageOpcode.InventorySnapshot,
        MessageOpcode.InventoryChanged
    };

    private static readonly MessageOpcode[] StorageOpcodes =
    {
        MessageOpcode.StorageSnapshot, MessageOpcode.StorageChanged, MessageOpcode.InventoryChanged
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
    public void Storage_ReachesOnlyItsCharacter_NotAnotherOfItsAccountInTheWorld()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId one = server.Connect();
        server.SignInWithCharacter(one, 1);
        server.Store.GiveItems(1, "item.material.slime_gel", 1, 5, 0);
        server.Store.Edit(1, coins: 20);
        server.SendEnterWorld(one, 1);
        server.TickUntil(() => server.SessionOf(one).State == SessionState.InWorld);
        ConnectionId eleven = server.Connect();
        server.SignIn(eleven, $"{TestServer.DevelopmentToken}1");
        server.TickUntil(() => server.SessionOf(eleven).Characters != null);
        server.Store.NextCharacterId = 11;
        server.SendCreateCharacter(eleven, "Tester11");
        server.TickUntil(() => server.SessionOf(eleven).Characters!.Any(owned => owned.Id == 11));
        server.SendEnterWorld(eleven, 11);
        server.TickUntil(() => server.SessionOf(eleven).State == SessionState.InWorld);
        NpcEntity keeper = server.NpcOf("npc.storekeeper");
        server.Place(one, keeper.Position.X + 2f, keeper.Position.Z);
        server.Place(eleven, keeper.Position.X + 2f, keeper.Position.Z + 1f);
        server.Tick(2);
        server.Transport.ClearSent();

        server.SendStorageOpen(one, keeper.Id, 1);
        server.Tick(2);
        server.SendStorageDeposit(
            one,
            keeper.Id,
            server.SessionOf(one).Character!.Inventory.Rows.Single().InventoryItem,
            5,
            2);
        server.Tick();
        server.TickUntil(() => server.SessionOf(one).Character!.Operation == null);

        Assert.That(
            server.Transport.ControlOpcodesSentTo(one),
            Is.SupersetOf(StorageOpcodes),
            "the depositor heard its storage and its bag");
        Assert.That(server.Transport.ControlOpcodesSentTo(eleven).Intersect(StorageOpcodes), Is.Empty);
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

    [Test]
    public void TheTrade_ReachesOnlyItsTwoTraders_NotAPlayerBesideThem()
    {
        var rig = new TradeRig();
        ConnectionId seven = rig.Enter(7, store => store.GiveItems(7, "item.material.slime_gel", 1, 5, 0));
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Server.Tick();
        rig.Server.Transport.ClearSent();

        rig.Open(seven, eight, "Tester7", "Tester8");
        rig.Offer(seven, rig.Server.SessionOf(seven).Character!.Inventory.Rows.Single().InventoryItem, 2);
        rig.Lock(seven);
        rig.Lock(eight);
        rig.Confirm(seven);
        rig.Confirm(eight);
        rig.Server.TickUntil(() => rig.Server.Trades.OpenTrades == 0);

        foreach (ConnectionId trader in new[] { seven, eight })
        {
            Assert.That(
                rig.Server.Transport.ControlOpcodesSentTo(trader),
                Is.SupersetOf(new[]
                    { MessageOpcode.TradeEvent, MessageOpcode.TradeSide, MessageOpcode.InventorySnapshot }),
                "each trader heard the trade");
        }

        Assert.That(rig.Server.Transport.ControlOpcodesSentTo(nine).Intersect(TradeOpcodes), Is.Empty);
    }
}
}
