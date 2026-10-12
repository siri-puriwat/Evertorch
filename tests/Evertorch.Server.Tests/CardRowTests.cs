using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Protocol 42 from the server (Network Protocol §6, §9): a row's cards reach its owner in the bag's snapshot, in
///     storage, and in a trade, where the partner sees them too, and the owner's sheet names what the worn equipment
///     adds to each primary statistic.
/// </summary>
[TestFixture]
public sealed class CardRowTests
{
    private const string Sword = "item.weapon.iron_sword";
    private const string Staff = "item.weapon.training_staff";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";

    private static readonly ItemDefinitionId[] Cards = { new(Keen), new(Bane) };

    private static long CardedSword(InMemoryGameStore store, long character)
    {
        store.GiveItems(character, Sword, 1, 1, 1);
        long sword = store.Stored(character).Items.Single(item => item.ItemDefinitionId == Sword).Id;
        store.Refine(character, sword, 4);
        store.Card(character, sword, Keen, Bane);
        return sword;
    }

    private static List<T> Read<T>(TestServer server, ConnectionId player, MessageOpcode opcode, TryRead<T> read)
        where T : class
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload, out T? value) ? value! : null!)
            .ToList();
    }

    private delegate bool TryRead<T>(byte[] payload, out T? value)
        where T : class;

    [Test]
    public void Sheet_WithAStaffWorn_NamesWhatItAddsToInt()
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, Staff, 1, 1, 0);
        server.Store.Wear(1, server.Store.Stored(1).Items.Single().Id, "Weapon");

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);

        CharacterSheet sheet = Read(
                server,
                player,
                MessageOpcode.CharacterSheet,
                (byte[] payload, out CharacterSheet? read) => CharacterSheet.TryRead(payload, out read))
            .Last();
        Assert.That(
            (sheet.EquipmentStr, sheet.EquipmentAgi, sheet.EquipmentVit, sheet.EquipmentInt, sheet.EquipmentDex,
                sheet.EquipmentLuk),
            Is.EqualTo(((ushort)0, (ushort)0, (ushort)0, (ushort)10, (ushort)0, (ushort)0)));
    }

    [Test]
    public void Snapshot_OfACardedRow_CarriesItsLevelAndCards_InSlotOrder()
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        long sword = CardedSword(server.Store, 1);

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);

        InventoryEntry row = Read(
                server,
                player,
                MessageOpcode.InventorySnapshot,
                (byte[] payload, out InventorySnapshot? read) => InventorySnapshot.TryRead(payload, out read))
            .SelectMany(part => part.Entries)
            .Single(entry => entry.InventoryItem == sword);
        Assert.That((row.RefineLevel, row.Cards), Is.EqualTo(((byte)4, Cards)));
        Assert.That(server.SessionOf(player).Character!.Inventory.Rows.Single().Cards, Is.EqualTo(Cards));
    }

    [Test]
    public void Storage_DepositOfACardedRow_TellsItsCards()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        long sword = CardedSword(server.Store, 1);
        server.Store.Edit(1, coins: 1000);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity keeper = server.NpcOf("npc.storekeeper");
        server.Place(player, keeper.Position.X + 2f, keeper.Position.Z);
        server.Tick(2);

        server.SendStorageDeposit(player, keeper.Id, sword, 1, 1);
        server.Tick();
        server.TickUntil(() => server.SessionOf(player).Character!.Operation == null);

        StorageEntry stored = Read(
                server,
                player,
                MessageOpcode.StorageChanged,
                (byte[] payload, out StorageChanged? read) => StorageChanged.TryRead(payload, out read))
            .Single()
            .Row;
        Assert.That((stored.Quantity, stored.RefineLevel, stored.Cards), Is.EqualTo((1u, (byte)4, Cards)));
    }

    [Test]
    public void Trade_OfACardedRow_ShowsItsCards_ToTheOwnerAndThePartner()
    {
        var rig = new TradeRig();
        long sword = 0;
        ConnectionId seven = rig.Enter(7, store => sword = CardedSword(store, 7));
        ConnectionId eight = rig.Enter(8);
        rig.Open(seven, eight, "Tester7", "Tester8");

        CommandRejectionReason offered = rig.RefusalOf(seven, rig.Offer(seven, sword, 1));

        TradeSide own = rig.Sides(seven).Last(side => side.Owner == TradeSideOwner.Own);
        TradeSide theirs = rig.Sides(eight).Last(side => side.Owner == TradeSideOwner.Partner);
        Assert.That(offered, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(
            (own.Entries.Single().InventoryItem, own.Entries.Single().Cards),
            Is.EqualTo((sword, Cards)));
        Assert.That(
            (theirs.Entries.Single().InventoryItem, theirs.Entries.Single().RefineLevel, theirs.Entries.Single().Cards),
            Is.EqualTo((0L, (byte)4, Cards)));
        Assert.That((theirs.Part, theirs.PartCount), Is.EqualTo(((byte)0, (byte)1)));
    }
}
}
