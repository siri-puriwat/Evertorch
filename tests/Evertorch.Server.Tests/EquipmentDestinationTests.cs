using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The equip's destination (Gameplay Systems §11.1, owner decision 2; Persistence §5): a piece goes to a position its
///     category may go to, an accessory to either accessory position, and a row worn anywhere is refused on the tick
///     thread before the store sees it. A held destination swaps in one operation that a full bag never refuses, and a
///     lost answer settles from the ledger in the destination.
/// </summary>
[TestFixture]
public sealed class EquipmentDestinationTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Staff = "item.weapon.training_staff";
    private const string Cloth = "item.armor.cloth";
    private const string SlimeGel = "item.material.slime_gel";
    private const uint Revision = 5;
    private const int MaxRows = 100;

    private static readonly Lazy<ServerContent> Gear = new(() => ServerContentLoader.Load(GearPackage.Build()));

    private delegate bool TryRead<T>(byte[] payload, out T message);

    private static List<T> Sent<T>(TestServer server, ConnectionId player, MessageOpcode opcode, TryRead<T> read)
    {
        var messages = new List<T>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == opcode && read(message.Payload, out T decoded))
            {
                messages.Add(decoded);
            }
        }

        return messages;
    }

    private static (uint Sequence, CommandRejectionReason Reason)[] Rejections(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.CommandRejected, (byte[] bytes, out CommandRejected read) =>
                CommandRejected.TryRead(bytes, out read))
            .Select(rejected => (rejected.CommandSequence, rejected.Reason))
            .ToArray();
    }

    private static List<InventoryChanged> Changes(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.InventoryChanged, (byte[] bytes, out InventoryChanged read) =>
        {
            bool isRead = InventoryChanged.TryRead(bytes, out InventoryChanged? message);
            read = message!;
            return isRead;
        });
    }

    private static InventoryEntry[] Baseline(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.InventorySnapshot, (byte[] bytes, out InventorySnapshot read) =>
            {
                bool isRead = InventorySnapshot.TryRead(bytes, out InventorySnapshot? message);
                read = message!;
                return isRead;
            })
            .SelectMany(part => part.Entries)
            .ToArray();
    }

    private static (long, uint, EquipmentSlot)[] Rows(InventoryChanged change)
    {
        return change.Changes.Select(row => (row.InventoryItem, row.Quantity, row.Slot)).ToArray();
    }

    private static long[] RowsOf(TestServer server, string item)
    {
        return server.Store.Stored(1)
            .Items.Where(stored => stored.ItemDefinitionId == item)
            .Select(stored => stored.Id)
            .ToArray();
    }

    private static string? SlotOf(TestServer server, long row)
    {
        return server.Store.Stored(1).Items.Single(stored => stored.Id == row).EquippedSlot;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    // Character 1 holds a row of each item, as earlier pickups would have left them, and enters the world.
    private static ConnectionId EnterHolding(TestServer server, params string[] items)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        foreach (string item in items)
        {
            server.Store.GiveItems(1, item, 1, 1, Revision);
        }

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    private static void Equip(
        TestServer server,
        ConnectionId player,
        long row,
        EquipmentSlot destination,
        uint sequence)
    {
        server.SendEquip(player, row, sequence, destination);
        server.TickUntil(() => CharacterOf(server, player).Operation == null);
    }

    [Test]
    public void Accessories_OfOneItem_AreWornOnePerPosition_AndEachComesOffAlone()
    {
        var server = new TestServer(content: Gear.Value);
        ConnectionId player = EnterHolding(server, GearPackage.Charm, GearPackage.Charm);
        long[] charms = RowsOf(server, GearPackage.Charm);
        server.Transport.ClearSent();

        Equip(server, player, charms[0], EquipmentSlot.Accessory1, 1);
        Equip(server, player, charms[1], EquipmentSlot.Accessory2, 2);
        server.SendUnequip(player, EquipmentSlot.Accessory1, 3);
        server.TickUntil(() => CharacterOf(server, player).Operation == null);
        server.SendUnequip(player, EquipmentSlot.Accessory1, 4);
        server.Tick();

        Assert.That(
            Changes(server, player).Select(Rows).ToArray(),
            Is.EqualTo(
                new[]
                {
                    new[] { (charms[0], 1u, EquipmentSlot.Accessory1) },
                    new[] { (charms[1], 1u, EquipmentSlot.Accessory2) },
                    new[] { (charms[0], 1u, EquipmentSlot.None) }
                }));
        Assert.That((SlotOf(server, charms[0]), SlotOf(server, charms[1])), Is.EqualTo(((string?)null, "Accessory2")));
        Assert.That(CharacterOf(server, player).Inventory.WornIn(EquipmentSlot.Accessory2), Is.EqualTo(charms[1]));
        Assert.That(
            Rejections(server, player),
            Is.EqualTo(new[] { (4u, CommandRejectionReason.InvalidTarget) }),
            "an empty position");
    }

    // The destination is checked before the job's weapon types (Gameplay Systems §11.1): a staff a Vanguard cannot
    // wield, sent to the headgear's position, is refused for the position.
    [Test]
    public void Equip_ByAJobThatCannotWieldTheWeapon_IsRefusedForItsDestinationFirst()
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.Edit(1, "job.vanguard", item: Staff);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        long staff = RowsOf(server, Staff).Single();
        server.Transport.ClearSent();

        server.SendEquip(player, staff, 1, EquipmentSlot.Headgear);
        server.Tick();
        server.SendEquip(player, staff, 2, EquipmentSlot.Weapon);
        server.Tick();

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    (1u, CommandRejectionReason.NotAllowedNow), (2u, CommandRejectionReason.RequirementNotMet)
                }));
        Assert.That(server.Store.EquipmentCommits, Is.Empty);
    }

    [Test]
    public void Equip_IntoAHeldPosition_SwapsInOneOperation_EvenWithTheBagFull()
    {
        var server = new TestServer(content: Gear.Value);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, GearPackage.Cap, 2, 1, Revision);
        server.Store.GiveItems(1, SlimeGel, MaxRows - 2, 1, Revision);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        long[] caps = RowsOf(server, GearPackage.Cap);
        Equip(server, player, caps[0], EquipmentSlot.Headgear, 1);
        int ledger = server.Store.LedgerCount;
        server.Transport.ClearSent();

        Equip(server, player, caps[1], EquipmentSlot.Headgear, 2);

        InventoryChanged swap = Changes(server, player).Single();
        Assert.That(
            Rows(swap),
            Is.EqualTo(new[] { (caps[1], 1u, EquipmentSlot.Headgear), (caps[0], 1u, EquipmentSlot.None) }));
        Assert.That(swap.NewRevision, Is.EqualTo(Revision + 2u));
        Assert.That(server.Store.LedgerCount, Is.EqualTo(ledger + 1));
        Assert.That((SlotOf(server, caps[1]), SlotOf(server, caps[0])), Is.EqualTo(("Headgear", (string?)null)));
        Assert.That(CharacterOf(server, player).Inventory.Rows, Has.Count.EqualTo(MaxRows), "the bag is full");
        Assert.That(Rejections(server, player), Is.Empty);
    }

    // Moving a worn accessory to the other position takes it off first: the tick thread refuses the move, so it never
    // reaches the store, whether the destination is empty or held, and the row's own position is refused the same way.
    [Test]
    public void Equip_OfARowWornAnywhere_IsRefusedWith3_WhetherTheDestinationIsEmptyOrHeld()
    {
        var server = new TestServer(content: Gear.Value);
        ConnectionId player = EnterHolding(server, GearPackage.Charm, GearPackage.Bead);
        long charm = RowsOf(server, GearPackage.Charm).Single();
        long bead = RowsOf(server, GearPackage.Bead).Single();
        Equip(server, player, charm, EquipmentSlot.Accessory1, 1);
        server.Transport.ClearSent();

        server.SendEquip(player, charm, 2, EquipmentSlot.Accessory2);
        server.Tick();
        Equip(server, player, bead, EquipmentSlot.Accessory2, 3);
        server.SendEquip(player, charm, 4, EquipmentSlot.Accessory2);
        server.Tick();
        server.SendEquip(player, charm, 5, EquipmentSlot.Accessory1);
        server.Tick();

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    (2u, CommandRejectionReason.NotAllowedNow), (4u, CommandRejectionReason.NotAllowedNow),
                    (5u, CommandRejectionReason.NotAllowedNow)
                }),
            "to the empty position, to the held one, and to its own");
        Assert.That(server.Store.EquipmentCommits, Has.Count.EqualTo(2), "the charm's and the bead's alone");
        Assert.That((SlotOf(server, charm), SlotOf(server, bead)), Is.EqualTo(("Accessory1", "Accessory2")));
        Assert.That(server.SessionOf(player).Violations!.Value, Is.Zero);
        Assert.That(
            server.AuditLogger.Entries.Count(entry => entry.EventId.Name == "CommandRefused"),
            Is.EqualTo(3));
    }

    [Test]
    public void Equip_OfEachCategory_InItsOwnPosition_IsWorn_AndTheBaselineNamesAllEight()
    {
        var server = new TestServer(content: Gear.Value);
        string[] pieces =
        {
            Sword, Cloth, GearPackage.Cap, GearPackage.Buckler, GearPackage.Cape, GearPackage.Boots,
            GearPackage.Charm, GearPackage.Bead
        };
        ConnectionId player = EnterHolding(server, pieces);
        long[] rows = pieces.Select(piece => RowsOf(server, piece).Single()).ToArray();

        for (int index = 0; index < pieces.Length; index++)
        {
            Equip(server, player, rows[index], EquipmentPositions.All[index], (uint)(index + 1));
        }

        server.Transport.ClearSent();
        server.SendInventoryResync(player);
        server.Tick();

        InventoryEntry[] baseline = Baseline(server, player);
        Assert.That(
            rows.Select(row => baseline.Single(entry => entry.InventoryItem == row).Slot),
            Is.EqualTo(EquipmentPositions.All));
        Assert.That(
            rows.Select(row => SlotOf(server, row)),
            Is.EqualTo(EquipmentPositions.All.Select(slot => slot.ToString())));
        Assert.That(
            EquipmentPositions.All.Select(slot => CharacterOf(server, player).Inventory.WornIn(slot)),
            Is.EqualTo(rows));
        Assert.That(Rejections(server, player), Is.Empty);
    }

    // After the row check (1), a destination the row's category cannot go to is refused with 3 (Gameplay Systems
    // §11.1), audited and never scored, and the store is never asked.
    [Test]
    public void Equip_ToAPositionItsCategoryCannotGoTo_IsRefusedWith3_AfterTheRowCheck()
    {
        var server = new TestServer(content: Gear.Value);
        (string Item, EquipmentSlot Destination)[] wrong =
        {
            (Sword, EquipmentSlot.Headgear), (Cloth, EquipmentSlot.Accessory1), (GearPackage.Cap, EquipmentSlot.Weapon),
            (GearPackage.Buckler, EquipmentSlot.Armor), (GearPackage.Cape, EquipmentSlot.Footwear),
            (GearPackage.Boots, EquipmentSlot.Garment), (GearPackage.Charm, EquipmentSlot.Shield),
            (SlimeGel, EquipmentSlot.Accessory2)
        };
        ConnectionId player = EnterHolding(server, wrong.Select(pair => pair.Item).ToArray());
        server.Transport.ClearSent();

        uint sequence = 0;
        foreach ((string item, EquipmentSlot destination) in wrong)
        {
            server.SendEquip(player, RowsOf(server, item).Single(), ++sequence, destination);
        }

        server.SendEquip(player, 999_999, ++sequence, EquipmentSlot.Headgear);
        server.Tick();

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                Enumerable.Range(1, wrong.Length)
                    .Select(number => ((uint)number, CommandRejectionReason.NotAllowedNow))
                    .Append((sequence, CommandRejectionReason.InvalidTarget))));
        Assert.That(server.Store.EquipmentCommits, Is.Empty);
        Assert.That(server.SessionOf(player).Violations!.Value, Is.Zero);
        Assert.That(
            server.AuditLogger.Entries.Count(entry => entry.EventId.Name == "CommandRefused"),
            Is.EqualTo(wrong.Length + 1));
    }

    [Test]
    public void Equip_WhoseAnswerIsLost_IsSettledFromTheLedger_InItsDestination()
    {
        var server = new TestServer(content: Gear.Value);
        ConnectionId player = EnterHolding(server, GearPackage.Charm, GearPackage.Bead);
        long charm = RowsOf(server, GearPackage.Charm).Single();
        long bead = RowsOf(server, GearPackage.Bead).Single();
        Equip(server, player, charm, EquipmentSlot.Accessory1, 1);
        int ledger = server.Store.LedgerCount;
        server.Store.AmbiguousEquipmentFailures = 100;
        server.Transport.ClearSent();

        server.SendEquip(player, bead, 2, EquipmentSlot.Accessory2);
        server.Tick(2);

        Assert.That(Changes(server, player), Is.Empty, "no answer yet, so nothing is told");
        Assert.That(server.Store.LedgerCount, Is.EqualTo(ledger + 1), "yet the equip was committed");

        server.Store.AmbiguousEquipmentFailures = 0;
        server.TickUntil(() => CharacterOf(server, player).Operation == null);

        Assert.That(server.Store.LedgerCount, Is.EqualTo(ledger + 1));
        Assert.That(Rows(Changes(server, player).Single()), Is.EqualTo(new[] { (bead, 1u, EquipmentSlot.Accessory2) }));
        Assert.That(CharacterOf(server, player).Inventory.WornIn(EquipmentSlot.Accessory1), Is.EqualTo(charm));
        Assert.That(CharacterOf(server, player).Inventory.WornIn(EquipmentSlot.Accessory2), Is.EqualTo(bead));
        Assert.That(Rejections(server, player), Is.Empty);
    }
}
}
