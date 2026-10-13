using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What the cards send (Network Protocol §9): an insertion and a removal reach their owner alone as its bag's change,
///     and no sheet, since nothing worn changed; a trade's sides, which name the cards, reach the two traders alone; an
///     inspection reaches its asker alone; observers hear of a worn carded sword only its item. And the card drops
///     replay: one seed gives the same drops, cards among them.
/// </summary>
[TestFixture]
public sealed class CardOutputTests
{
    private const string Smith = "npc.smith";
    private const string Sword = "item.weapon.iron_sword";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";

    private static readonly MessageOpcode[] CardCarriers =
    {
        MessageOpcode.InventorySnapshot, MessageOpcode.InventoryChanged, MessageOpcode.TradeSide,
        MessageOpcode.StorageSnapshot, MessageOpcode.StorageChanged, MessageOpcode.InspectedEquipment
    };

    // Character 1 holds an Iron Sword carrying Bane and two Keen cards; character 2 enters beside it. Both stand by the
    // Smith.
    private static (TradeRig Rig, ConnectionId Owner, ConnectionId Observer) Enter()
    {
        var rig = new TradeRig(new TestServer(withNpcs: true));
        ConnectionId owner = rig.Enter(
            1,
            store =>
            {
                store.GiveItems(1, Sword, 1, 1, 5);
                store.Card(1, store.Stored(1).Items.Last().Id, Bane);
                store.GiveItems(1, Keen, 1, 2, 5);
                store.Edit(1, coins: 1000);
            });
        ConnectionId observer = rig.Enter(2);
        NpcEntity smith = rig.Server.NpcOf(Smith);
        rig.Server.Place(owner, smith.Position.X + 2f, smith.Position.Z);
        rig.Server.Place(observer, smith.Position.X + 2f, smith.Position.Z + 1f);
        rig.Server.Tick(2);
        rig.Server.Transport.ClearSent();
        return (rig, owner, observer);
    }

    private static MessageOpcode[] Heard(TestServer server, ConnectionId connection)
    {
        return server.Transport.SentTo(connection)
            .Select(message => message.Opcode)
            .Where(opcode => opcode != MessageOpcode.EntitySnapshot)
            .ToArray();
    }

    private static long RowOf(TestServer server, ConnectionId connection, string item)
    {
        return server.SessionOf(connection).Character!.Inventory.Rows.First(row => row.Item.Value == item)
            .InventoryItem;
    }

    private static void Settle(TestServer server, ConnectionId connection)
    {
        CharacterSession character = server.SessionOf(connection).Character!;
        server.Tick();
        server.TickUntil(() => character.Operation == null && server.Persistence.PendingJobs == 0);
        server.Tick(2);
    }

    // Each live monster of the training ground and field falls in ID order, round after round; the items each death
    // drops, in order.
    private static List<string> Kill(ulong seed)
    {
        var server = new TestServer(withEveryMap: true, withMonsters: true, randomSeed: seed, cardChanceScale: 200);
        var drops = new List<string>();
        for (int round = 0; round < 8; round++)
        {
            foreach (string id in new[] { "map.training_ground", "map.training_field" })
            {
                server.World.TryGetMap(new MapDefinitionId(id), out MapInstance? map);
                foreach (MonsterEntity monster in map!.Monsters.Where(monster => !monster.IsDead)
                             .OrderBy(monster => monster.Id.Value)
                             .ToArray())
                {
                    var before = new HashSet<EntityId>(map.ItemDrops.Select(drop => drop.Id));
                    server.Combat.Kill(map, monster, null, server.CurrentTick);
                    drops.AddRange(map.ItemDrops.Where(drop => !before.Contains(drop.Id))
                        .Select(drop => $"{monster.Definition.Id.Value} {drop.Item.Value}"));
                }
            }

            server.Tick(TestServer.TickRate * 40);
        }

        return drops;
    }

    // The owner wears the carded sword: an observer hears its item, which every observer of a weapon hears, and never
    // a message that names a card.
    [Test]
    public void ACardedSwordWorn_ShowsAnObserverItsItemAlone()
    {
        (TradeRig rig, ConnectionId owner, ConnectionId observer) = Enter();
        TestServer server = rig.Server;
        server.Transport.ClearSent();

        server.SendEquip(owner, RowOf(server, owner, Sword), rig.Next(owner));
        Settle(server, owner);

        MessageOpcode[] observed = Heard(server, observer);
        Assert.That(observed, Does.Contain(MessageOpcode.WornWeaponChanged), "the item, as before the cards");
        Assert.That(observed.Intersect(CardCarriers), Is.Empty, "no message that names a card");
    }

    // A trade's sides name the cards and reach the two traders alone; a third player beside them hears nothing of it.
    [Test]
    public void ATradesSides_NameTheCards_ToTheTwoTradersAlone()
    {
        (TradeRig rig, ConnectionId owner, ConnectionId partner) = Enter();
        TestServer server = rig.Server;
        ConnectionId third = rig.Enter(3);
        server.Place(third, server.PlayerOf(owner).Position.X, server.PlayerOf(owner).Position.Z - 1f);
        server.Tick(2);
        server.Transport.ClearSent();

        rig.Open(owner, partner, "Tester1", "Tester2");
        rig.Offer(owner, RowOf(server, owner, Sword), 1);
        server.Tick(2);

        Assert.That(
            rig.Sides(partner).Last(side => side.Entries.Count > 0).Entries.Single().Cards.Select(card => card.Value),
            Is.EqualTo(new[] { Bane }),
            "the partner sees the card");
        Assert.That(rig.Sides(owner).Any(side => side.Entries.Count > 0), Is.True, "and the owner its own offer");
        Assert.That(Heard(server, third).Intersect(CardCarriers), Is.Empty, "a third player nothing");
    }

    [Test]
    public void AnInsertionAndARemoval_ReachTheirOwnerAlone_AsItsBagsChange()
    {
        (TradeRig rig, ConnectionId owner, ConnectionId observer) = Enter();
        TestServer server = rig.Server;
        long sword = RowOf(server, owner, Sword);

        server.SendInsertCard(owner, RowOf(server, owner, Keen), sword, rig.Next(owner));
        Settle(server, owner);
        MessageOpcode[] inserted = Heard(server, owner);
        MessageOpcode[] observedInsertion = Heard(server, observer);
        server.Transport.ClearSent();
        server.SendRemoveCard(owner, server.NpcOf(Smith).Id, sword, 1, Bane, rig.Next(owner));
        Settle(server, owner);
        MessageOpcode[] removed = Heard(server, owner);

        Assert.That(inserted, Is.EqualTo(new[] { MessageOpcode.InventoryChanged }), "the bag, and no sheet");
        Assert.That(removed, Is.EqualTo(new[] { MessageOpcode.InventoryChanged }), "the bag and the coins");
        Assert.That(observedInsertion.Concat(Heard(server, observer)), Is.Empty, "nothing of another's cards");
    }

    // An inspection reaches its asker alone: neither the inspected nor a third player beside them hears it.
    [Test]
    public void AnInspection_ReachesItsAskerAlone()
    {
        (TradeRig rig, ConnectionId owner, ConnectionId asker) = Enter();
        TestServer server = rig.Server;
        ConnectionId third = rig.Enter(3);
        server.Place(third, server.PlayerOf(owner).Position.X, server.PlayerOf(owner).Position.Z - 1f);
        server.SendEquip(owner, RowOf(server, owner, Sword), rig.Next(owner));
        Settle(server, owner);
        server.Transport.ClearSent();

        server.SendInspectPlayer(asker, server.PlayerOf(owner).Id, rig.Next(asker));
        server.Tick(2);

        Assert.That(Heard(server, asker), Is.EqualTo(new[] { MessageOpcode.InspectedEquipment }));
        Assert.That(Heard(server, owner).Concat(Heard(server, third)), Is.Empty, "nobody else hears it");
    }

    // One seed, the same kills, the same drops (Gameplay Systems §10): every monster of the two training maps falls
    // eight times, each card's chance raised to a fifth, so cards and their absence both appear.
    [Test]
    public void CardDrops_UnderOneSeed_Replay()
    {
        List<string> drops = Kill(17);
        List<string> replayed = Kill(17);

        Assert.That(replayed, Is.EqualTo(drops), "the same draws");
        Assert.That(drops.Count(drop => drop.Contains(" item.card.")), Is.GreaterThan(0), "cards among them");
        Assert.That(drops.Count(drop => !drop.Contains(" item.card.")), Is.GreaterThan(0), "and other drops");
    }
}
}
