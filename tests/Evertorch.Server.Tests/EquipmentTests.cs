using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Equipping and unequipping (Gameplay Systems §11.1, Persistence §5, Network Protocol §8, §11): the checks in
///     their order, one inventory operation at a time, the commit before anyone is told, a swap as one operation, a
///     lost answer settled from the ledger, and the statistics following the slots, from the first tick of a load.
/// </summary>
[TestFixture]
public sealed class EquipmentTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Staff = "item.weapon.training_staff";
    private const string Cloth = "item.armor.cloth";
    private const string SlimeGel = "item.material.slime_gel";
    private const int Revision = 5;

    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");

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

    private static string[] WornWeapons(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.WornWeaponChanged, (byte[] bytes, out WornWeaponChanged read) =>
            {
                bool isRead = WornWeaponChanged.TryRead(bytes, out WornWeaponChanged? message);
                read = message!;
                return isRead;
            })
            .Select(changed => changed.WornWeapon)
            .ToArray();
    }

    private static List<CharacterHealth> Healths(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.CharacterHealth, (byte[] bytes, out CharacterHealth read) =>
            CharacterHealth.TryRead(bytes, out read));
    }

    private static (long, uint, EquipmentSlot)[] Rows(IReadOnlyList<InventoryEntry> rows)
    {
        return rows.Select(row => (row.InventoryItem, row.Quantity, row.Slot)).ToArray();
    }

    private static (long, uint, EquipmentSlot)[] Rows(InventoryChanged change)
    {
        return Rows(change.Changes);
    }

    private static long RowOf(TestServer server, long character, string item)
    {
        return server.Store.Stored(character).Items.Single(stored => stored.ItemDefinitionId == item).Id;
    }

    private static string? SlotOf(TestServer server, long character, long row)
    {
        return server.Store.Stored(character).Items.Single(stored => stored.Id == row).EquippedSlot;
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

    private static void Equip(TestServer server, ConnectionId player, long row, uint sequence)
    {
        server.SendEquip(player, row, sequence);
        server.TickUntil(() => CharacterOf(server, player).Operation == null);
    }

    private static bool Hit(TestServer server, ConnectionId player, PlayerEntity entity)
    {
        return Sent(server, player, MessageOpcode.Damage, (byte[] bytes, out Damage read) =>
                Damage.TryRead(bytes, out read))
            .Any(damage => damage.Target == entity.Id);
    }

    // A crawler's lowest roll is 8: cloth armor's hard defense 8 takes it to 7 (8 × 4008 ÷ 4080), and the soft
    // defense 4 to 3; without the armor it is 4 (equipment research note).
    [Test]
    public void ClothArmor_IsTheWearersHardDefense_AgainstACrawler()
    {
        var server = new TestServer(withEveryMap: true, withMonsters: true, combatRandom: new SureHitRandom());
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, Cloth, 1, 1, Revision);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        Equip(server, player, RowOf(server, 1, Cloth), 1);
        PlayerEntity entity = server.PlayerOf(player);
        Assert.That(entity.Armor!.Defense, Is.EqualTo(8));

        server.World.TryGetMap(Ground, out MapInstance? ground);
        entity.Position = ground!.Definition.Portals.Single().Center;
        server.Tick(2);
        Assert.That(CharacterOf(server, player).Map.Definition.Id, Is.EqualTo(Field), "crossed");
        MonsterEntity[] crawlers = CharacterOf(server, player).Map.Monsters
            .Where(monster => monster.Definition.Id.Value == "monster.forest_crawler")
            .ToArray();
        for (int index = 1; index < crawlers.Length; index++)
        {
            crawlers[index].Position = new WorldPosition(20f, 0f, -20f);
        }

        var home = new WorldPosition(8f, 0f, 12f);
        crawlers[0].Position = home;
        entity.Position = new WorldPosition(home.X + 1.2f, 0f, home.Z);
        entity.CurrentHealth = 1_000_000;
        server.Transport.ClearSent();

        for (int tick = 0; tick < 200 && !Hit(server, player, entity); tick++)
        {
            server.Tick();
        }

        Damage hit = Sent(server, player, MessageOpcode.Damage, (byte[] bytes, out Damage read) =>
                Damage.TryRead(bytes, out read))
            .First(damage => damage.Target == entity.Id);
        Assert.That(hit.Source, Is.EqualTo(crawlers[0].Id));
        Assert.That(hit.Amount, Is.EqualTo(3u));
    }

    [Test]
    public void Commit_ThatNeverHappened_IsRefusedAsServiceUnavailableOnceTheLedgerAnswers()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Sword);
        long sword = RowOf(server, 1, Sword);
        // The party's load, queued on entry, is back before the outage begins.
        server.Tick();
        server.Transport.ClearSent();

        server.SendEquip(player, sword, 1);
        server.Store.IsUnavailable = true;
        server.Tick(3);

        Assert.That(CharacterOf(server, player).Operation, Is.Not.Null, "the ledger cannot be asked yet");

        server.Store.IsUnavailable = false;
        server.TickUntil(() => CharacterOf(server, player).Operation == null);

        Assert.That(Rejections(server, player), Is.EqualTo(new[] { (1u, CommandRejectionReason.ServiceUnavailable) }));
        Assert.That(Changes(server, player), Is.Empty);
        Assert.That(SlotOf(server, 1, sword), Is.Null);
        Assert.That(server.PlayerOf(player).Weapon, Is.Null);
    }

    [Test]
    public void Commit_WhoseAnswerIsLost_IsSettledFromTheLedgerOnce_WithBothRowsOfTheSwap()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Sword, Staff);
        long sword = RowOf(server, 1, Sword);
        long staff = RowOf(server, 1, Staff);
        Equip(server, player, sword, 1);
        int ledger = server.Store.LedgerCount;
        server.Store.AmbiguousEquipmentFailures = 100;
        server.Transport.ClearSent();

        server.SendEquip(player, staff, 2);
        server.Tick(2);

        Assert.That(Changes(server, player), Is.Empty, "no answer yet, so nothing is told");
        Assert.That(server.Store.LedgerCount, Is.EqualTo(ledger + 1), "yet the swap was committed");
        Assert.That(
            server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "InventoryOperationUnsettled").Fields,
            Does.ContainKey("OperationId").And.ContainKey("Character").And.ContainKey("Connection"));

        server.Store.AmbiguousEquipmentFailures = 0;
        server.TickUntil(() => CharacterOf(server, player).Operation == null);

        Assert.That(server.Store.LedgerCount, Is.EqualTo(ledger + 1));
        Assert.That(
            Rows(Changes(server, player).Single()),
            Is.EqualTo(new[] { (staff, 1u, EquipmentSlot.Weapon), (sword, 1u, EquipmentSlot.None) }));
        Assert.That(CharacterOf(server, player).Inventory.WornIn(EquipmentSlot.Weapon), Is.EqualTo(staff));
        Assert.That(server.PlayerOf(player).MaxSpirit, Is.EqualTo(26));
        Assert.That(Rejections(server, player), Is.Empty);
    }

    [Test]
    public void Disconnect_WithAnEquipInFlightAndNoGrace_RemovesTheCharacterOnlyAfterItSettles()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Sword);
        long sword = RowOf(server, 1, Sword);
        server.RunsPersistence = false;
        server.SendEquip(player, sword, 1);
        server.Tick();
        server.Disconnect(player);
        server.Tick();

        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(1));

        server.RunsPersistence = true;
        server.Tick(3);

        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(SlotOf(server, 1, sword), Is.EqualTo("Weapon"));
        Assert.That(server.Store.Checkpoints, Has.Count.EqualTo(1));
    }

    [Test]
    public void Equip_IntoAFullSlot_SwapsInOneOperation_TellingBothRowsAndTheNewMaximums()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Sword, Staff);
        long sword = RowOf(server, 1, Sword);
        long staff = RowOf(server, 1, Staff);
        Equip(server, player, sword, 1);
        int ledger = server.Store.LedgerCount;
        server.Transport.ClearSent();

        Equip(server, player, staff, 2);

        InventoryChanged swap = Changes(server, player).Single();
        Assert.That(
            Rows(swap),
            Is.EqualTo(new[] { (staff, 1u, EquipmentSlot.Weapon), (sword, 1u, EquipmentSlot.None) }));
        Assert.That(swap.NewRevision, Is.EqualTo(Revision + 2u));
        Assert.That(server.Store.LedgerCount, Is.EqualTo(ledger + 1));
        Assert.That((SlotOf(server, 1, staff), SlotOf(server, 1, sword)), Is.EqualTo(("Weapon", (string?)null)));
        PlayerEntity entity = server.PlayerOf(player);
        Assert.That(
            (entity.Stats.AttackSpeed, entity.MaxSpirit, entity.Stats.VariableCastPermille),
            Is.EqualTo((143, 26, 782)),
            "the staff's penalty 54 and INT +10");
        Assert.That(Healths(server, player).Single().MaximumSpirit, Is.EqualTo(26u));
    }

    [Test]
    public void Equip_IsCommittedBeforeAnyoneIsTold_ThenToldToTheOwnerAlone_AndTheWeaponCounts()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Sword);
        ConnectionId other = server.EnterWorld(2);
        long sword = RowOf(server, 1, Sword);
        server.Transport.ClearSent();
        server.RunsPersistence = false;

        server.SendEquip(player, sword, 1);
        server.Tick(2);

        Assert.That(Changes(server, player), Is.Empty, "no answer yet, so nothing is told");
        Assert.That(WornWeapons(server, other), Is.Empty, "nor does anyone near see the sword yet");
        Assert.That(server.PlayerOf(player).Weapon, Is.Null);
        Assert.That(CharacterOf(server, player).Operation!.Kind, Is.EqualTo(InventoryOperationKind.Equip));

        server.RunsPersistence = true;
        server.Tick(2);

        InventoryChanged change = Changes(server, player).Single();
        Assert.That((change.PriorRevision, change.NewRevision), Is.EqualTo((Revision, Revision + 1u)));
        Assert.That(Rows(change), Is.EqualTo(new[] { (sword, 1u, EquipmentSlot.Weapon) }));
        Assert.That(Changes(server, other), Is.Empty, "the inventory is the owner's alone");
        Assert.That(WornWeapons(server, other), Is.EqualTo(new[] { Sword }), "everyone near sees the sword once");
        Assert.That(WornWeapons(server, player), Is.Empty, "the owner learns it from its inventory");
        Assert.That(SlotOf(server, 1, sword), Is.EqualTo("Weapon"));
        Assert.That(server.Store.EquipmentCommits, Has.Count.EqualTo(1));
        Assert.That(CharacterOf(server, player).Inventory.WornIn(EquipmentSlot.Weapon), Is.EqualTo(sword));
        Assert.That(server.PlayerOf(player).Weapon!.Attack, Is.EqualTo(20));
        Assert.That(server.PlayerOf(player).Stats.AttackSpeed, Is.EqualTo(147), "the sword's penalty 50, not 44");
        Assert.That(Healths(server, player), Is.Empty, "the sword changes neither maximum");
        Assert.That(Rejections(server, player), Is.Empty);
    }

    [Test]
    public void Load_OfACharacterWearingEquipment_CountsItFromTheFirstTick()
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, Staff, 1, 1, Revision);
        long staff = RowOf(server, 1, Staff);
        server.Store.Wear(1, staff, "Weapon");
        server.Store.Edit(1, spirit: 26);

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);

        WorldEntered entered = Sent(server, player, MessageOpcode.WorldEntered, (byte[] bytes, out WorldEntered read) =>
        {
            bool isRead = WorldEntered.TryRead(bytes, out WorldEntered? message);
            read = message!;
            return isRead;
        }).Single();
        Assert.That(
            (entered.CurrentSpirit, entered.MaximumSpirit),
            Is.EqualTo((26u, 26u)),
            "the stored SP is held to the staff's maximum, not the bare 24");
        Assert.That(server.PlayerOf(player).Stats.AttackSpeed, Is.EqualTo(143));
        Assert.That(CharacterOf(server, player).Inventory.WornIn(EquipmentSlot.Weapon), Is.EqualTo(staff));
        InventorySnapshot baseline = Sent(server, player, MessageOpcode.InventorySnapshot,
            (byte[] bytes, out InventorySnapshot read) =>
            {
                bool isRead = InventorySnapshot.TryRead(bytes, out InventorySnapshot? message);
                read = message!;
                return isRead;
            }).Single();
        Assert.That(Rows(baseline.Entries), Is.EqualTo(new[] { (staff, 1u, EquipmentSlot.Weapon) }), "the baseline");
    }

    [Test]
    public void Load_OfARowWornInASlotItsItemDoesNotFill_IsRefused_AndTheDataIsKept()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.GiveItems(7, SlimeGel, 1, 1, 1);
        long gel = RowOf(server, 7, SlimeGel);
        server.Store.Wear(7, gel, "Weapon");

        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, 7);
        server.SendEnterWorld(connection, 7);
        server.TickUntil(() => server.SessionOf(connection).State != SessionState.EnteringWorld);

        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(
            server.Log.Entries
                .Where(entry => entry.EventId.Name == "CharacterContentMismatch")
                .Select(entry => entry.Message),
            Has.Some.Contains("weapon 'item.material.slime_gel'"));
        Assert.That(SlotOf(server, 7, gel), Is.EqualTo("Weapon"));
    }

    [Test]
    public void OneInventoryOperationAtATime_APickupAndAnEquipRefuseEachOtherWithReason9()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Sword);
        long sword = RowOf(server, 1, Sword);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(SlimeGel),
            1,
            server.PlayerOf(player).Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.Transport.ClearSent();
        server.RunsPersistence = false;

        server.SendEquip(player, sword, 1);
        server.SendPickup(player, drop.Id, 2);
        server.Tick();
        server.RunsPersistence = true;
        server.TickUntil(() => CharacterOf(server, player).Operation == null);
        server.RunsPersistence = false;
        server.SendPickup(player, drop.Id, 3);
        server.SendUnequip(player, EquipmentSlot.Weapon, 4);
        server.Tick();

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    (2u, CommandRejectionReason.ItemActionInFlight), (4u, CommandRejectionReason.ItemActionInFlight)
                }));
        Assert.That(drop.IsReserved, Is.True, "the second pickup was taken");
        server.RunsPersistence = true;
        server.Tick(2);
    }

    [Test]
    public void Refusals_AreAnsweredWithEachReasonInTheOrderOfTheChecks_AuditedAndNeverScored()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Sword, SlimeGel);
        server.EnterWorld(2);
        server.Store.GiveItems(2, Sword, 1, 1, 1);
        long theirs = RowOf(server, 2, Sword);
        long sword = RowOf(server, 1, Sword);
        long gel = RowOf(server, 1, SlimeGel);
        server.Transport.ClearSent();
        server.RunsPersistence = false;

        server.SendEquip(player, sword, 1);
        server.SendEquip(player, 999999, 2);
        server.SendUnequip(player, EquipmentSlot.Armor, 3);
        server.Tick();
        server.RunsPersistence = true;
        server.TickUntil(() => CharacterOf(server, player).Operation == null);
        server.SendEquip(player, theirs, 4);
        server.SendEquip(player, 999999, 5);
        server.SendEquip(player, gel, 6);
        server.SendEquip(player, sword, 7);
        server.SendUnequip(player, EquipmentSlot.Armor, 8);
        server.Tick();

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    (2u, CommandRejectionReason.ItemActionInFlight), (3u, CommandRejectionReason.ItemActionInFlight),
                    (4u, CommandRejectionReason.InvalidTarget), (5u, CommandRejectionReason.InvalidTarget),
                    (6u, CommandRejectionReason.NotAllowedNow), (7u, CommandRejectionReason.NotAllowedNow),
                    (8u, CommandRejectionReason.InvalidTarget)
                }),
            "in flight before anything else; another's row and none; a material; worn already; an empty slot");
        Assert.That(server.SessionOf(player).Violations!.Value, Is.Zero);
        Assert.That(
            server.AuditLogger.Entries.Count(entry => entry.EventId.Name == "CommandRefused"),
            Is.EqualTo(7));
        Assert.That(server.Store.EquipmentCommits, Has.Count.EqualTo(1));
        Assert.That(SlotOf(server, 2, theirs), Is.Null);
    }

    // Equipment research note's vectors against the training slime (hard defense 2) with every draw at its lowest:
    // unarmed 13 every 940 ms; with the sword (weapon part 19) 32 every 1,060 ms. A swing that began before the equip
    // settled keeps its interval.
    [Test]
    public void Sword_PickedUpAndEquipped_ChangesTheIntervalAndTheDamageAsTheVectorsSay()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 10_000;
        rig.Attack(rig.Slime.Id);
        rig.Server.Tick(40);
        ItemDropEntity drop = rig.Server.World.SpawnItemDrop(
            rig.Map,
            new ItemDefinitionId(Sword),
            1,
            rig.Entity.Position,
            rig.Server.CurrentTick,
            long.MaxValue,
            default);
        rig.Server.Tick();
        rig.Server.SendPickup(rig.Player, drop.Id, 100);
        rig.Server.TickUntil(() => CharacterOf(rig.Server, rig.Player).Inventory.Rows.Count == 1);
        long sword = CharacterOf(rig.Server, rig.Player).Inventory.Rows.Single().InventoryItem;

        rig.Server.SendEquip(rig.Player, sword, 101);
        rig.Server.TickUntil(() => rig.Entity.Weapon != null);
        uint settled = rig.Server.CurrentTick;
        rig.Server.Tick(60);

        List<AttackStarted> starts = rig.Starts();
        Assert.That(
            starts.Where(start => start.StartTick < settled).Select(start => start.Timing.Interval),
            Is.Not.Empty.And.All.EqualTo(TimeSpan.FromMilliseconds(940)));
        Assert.That(
            starts.Where(start => start.StartTick >= settled).Select(start => start.Timing.Interval).ToArray(),
            Has.Length.GreaterThanOrEqualTo(2).And.All.EqualTo(TimeSpan.FromMilliseconds(1060)));
        List<Damage> damages = rig.Damages();
        Assert.That(damages.First().Amount, Is.EqualTo(13u), "unarmed");
        Assert.That(damages.Last().Amount, Is.EqualTo(32u), "armed");
    }

    [Test]
    public void Unequip_EmptiesTheSlot_KeepsTheItem_AndTheStatisticsFollow()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Staff);
        ConnectionId other = server.EnterWorld(2);
        long staff = RowOf(server, 1, Staff);
        Equip(server, player, staff, 1);
        server.PlayerOf(player).CurrentSpirit = 26;
        server.Tick();
        server.Transport.ClearSent();

        server.SendUnequip(player, EquipmentSlot.Weapon, 2);
        server.TickUntil(() => CharacterOf(server, player).Operation == null);
        server.Tick();

        Assert.That(Rows(Changes(server, player).Single()), Is.EqualTo(new[] { (staff, 1u, EquipmentSlot.None) }));
        Assert.That(server.Store.Stored(1).Items.Select(item => (item.Id, item.EquippedSlot)),
            Is.EqualTo(new[] { (staff, (string?)null) }));
        PlayerEntity entity = server.PlayerOf(player);
        Assert.That(entity.Weapon, Is.Null);
        Assert.That((entity.Stats.AttackSpeed, entity.MaxSpirit), Is.EqualTo((153, 24)));
        CharacterHealth health = Healths(server, player).Single();
        Assert.That((health.CurrentSpirit, health.MaximumSpirit), Is.EqualTo((24u, 24u)), "SP held to the new maximum");
        Assert.That(WornWeapons(server, other), Is.EqualTo(new[] { string.Empty }), "others see an empty hand once");
    }

    [Test]
    public void WornWeapon_IsToldToOthersOnlyWhenTheWeaponsItemChanges()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, Sword, Cloth);
        ConnectionId other = server.EnterWorld(2);
        server.Tick();
        server.Transport.ClearSent();

        Equip(server, player, RowOf(server, 1, Sword), 1);
        server.Tick(2);
        Assert.That(WornWeapons(server, other), Is.EqualTo(new[] { Sword }), "the sword, once");
        server.Transport.ClearSent();
        Equip(server, player, RowOf(server, 1, Cloth), 2);
        server.Tick(2);

        Assert.That(server.PlayerOf(player).Armor, Is.Not.Null, "the armor is worn");
        Assert.That(WornWeapons(server, other), Is.Empty, "an armor, beside the same sword, shows in no hand anew");
    }
}
}
