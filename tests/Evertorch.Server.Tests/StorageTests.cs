using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The account's storage at the Storekeeper (Gameplay Systems §11.4; Persistence §5; Network Protocol §9, §11): a
///     read answered in parts with the fee, deposits and withdrawals checked in order and committed before the owner hears
///     the bag's change and then the storage's, and each refusal answered with its reason and never scored.
/// </summary>
[TestFixture]
public sealed class StorageTests
{
    private const string Storekeeper = "npc.storekeeper";
    private const string Quartermaster = "npc.quartermaster";
    private const string Gel = "item.material.slime_gel";
    private const string Sword = "item.weapon.training_sword";
    private const string Mantle = "item.armor.monarch_mantle";

    private sealed class Counter
    {
        public Counter(TestServer server, ConnectionId player, EntityId npc)
        {
            Server = server;
            Player = player;
            Npc = npc;
        }

        public TestServer Server { get; }

        public ConnectionId Player { get; }

        public EntityId Npc { get; }

        public CharacterSession Character => Server.SessionOf(Player).Character!;

        public long RowOf(string item)
        {
            return Character.Inventory.Rows.First(row => row.Item.Value == item).InventoryItem;
        }

        // Answers the command sent last, once its tick and, if it passed, its commit or its read have run.
        public CommandRejectionReason Answer()
        {
            Server.Tick();
            Server.TickUntil(() => Character.Operation == null && !Character.IsReadingStorage);
            return Server.Transport.ControlSentTo(Player)
                .Where(message => message.Opcode == MessageOpcode.CommandRejected)
                .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
                .DefaultIfEmpty(CommandRejectionReason.None)
                .Last();
        }

        public List<StorageSnapshot> Parts()
        {
            return Server.Transport.ControlSentTo(Player)
                .Where(message => message.Opcode == MessageOpcode.StorageSnapshot)
                .Select(message => StorageSnapshot.TryRead(message.Payload, out StorageSnapshot? read) ? read! : null)
                .Select(part => part!)
                .ToList();
        }

        public List<StorageChanged> StorageChanges()
        {
            return Server.Transport.ControlSentTo(Player)
                .Where(message => message.Opcode == MessageOpcode.StorageChanged)
                .Select(message => StorageChanged.TryRead(message.Payload, out StorageChanged? read) ? read! : null)
                .Select(change => change!)
                .ToList();
        }

        public List<InventoryChanged> Changes()
        {
            return Server.Transport.ControlSentTo(Player)
                .Where(message => message.Opcode == MessageOpcode.InventoryChanged)
                .Select(message => InventoryChanged.TryRead(message.Payload, out InventoryChanged? read) ? read! : null)
                .Select(change => change!)
                .ToList();
        }

        public MessageOpcode[] Told()
        {
            return Server.Transport.ControlSentTo(Player)
                .Select(message => message.Opcode)
                .Where(opcode => opcode == MessageOpcode.InventoryChanged || opcode == MessageOpcode.StorageChanged)
                .ToArray();
        }
    }

    // Character 1 holds the coins and what prepare gives it, as earlier sessions would have left them, enters the
    // training ground, and stands the given distance east of the NPC, which it knows.
    private static Counter Enter(
        long coins,
        float distance = 2f,
        string npc = Storekeeper,
        Action<InMemoryGameStore>? prepare = null,
        Action<TestServer>? before = null)
    {
        var server = new TestServer(withNpcs: true);
        before?.Invoke(server);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        prepare?.Invoke(server.Store);
        server.Store.Edit(1, coins: coins);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity keeper = server.NpcOf(npc);
        server.Place(player, keeper.Position.X + distance, keeper.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();
        return new Counter(server, player, keeper.Id);
    }

    private static Action<InMemoryGameStore> Holding(string item, int quantity)
    {
        return store => store.GiveItems(1, item, 1, quantity, 5);
    }

    private static void AssertNotScored(Counter counter)
    {
        Assert.That(counter.Server.SessionOf(counter.Player).Violations!.Value, Is.Zero, "a refusal is never scored");
    }

    private static IEnumerable<TestCaseData> OpenRefusals()
    {
        yield return new TestCaseData(2f, Quartermaster, CommandRejectionReason.InvalidTarget)
            .SetName("Open at an NPC that keeps no storage is 1");
        yield return new TestCaseData(3.6f, Storekeeper, CommandRejectionReason.OutOfRange)
            .SetName("Open beyond three and a half metres is 2");
    }

    [TestCaseSource(nameof(OpenRefusals))]
    public void Open_ThatCannotBe_IsRefused(float distance, string npc, CommandRejectionReason expected)
    {
        Counter counter = Enter(0, distance, npc);

        counter.Server.SendStorageOpen(counter.Player, counter.Npc, 1);

        Assert.That(counter.Answer(), Is.EqualTo(expected));
        Assert.That(counter.Parts(), Is.Empty);
        AssertNotScored(counter);
    }

    // Each case fails one check and passes the ones before it (Gameplay Systems §11.4).
    private static IEnumerable<TestCaseData> DepositRefusals()
    {
        yield return new TestCaseData(100L, 2f, Quartermaster, "gel", 1u, CommandRejectionReason.InvalidTarget)
            .SetName("Deposit at an NPC that keeps no storage is 1");
        yield return new TestCaseData(0L, 3.6f, Storekeeper, "gel", 1u, CommandRejectionReason.OutOfRange)
            .SetName("Deposit out of reach without coins is 2");
        yield return new TestCaseData(0L, 2f, Storekeeper, "none", 1u, CommandRejectionReason.InvalidTarget)
            .SetName("Deposit of a row not held without coins is 1");
        yield return new TestCaseData(0L, 2f, Storekeeper, "worn", 1u, CommandRejectionReason.NotAllowedNow)
            .SetName("Deposit of a worn row without coins is 3");
        yield return new TestCaseData(0L, 2f, Storekeeper, "gel", 13u, CommandRejectionReason.NotAllowedNow)
            .SetName("Deposit of more than the row holds without coins is 3");
        yield return new TestCaseData(19L, 2f, Storekeeper, "gel", 12u, CommandRejectionReason.NotEnoughCoins)
            .SetName("Deposit with a coin short of the fee is 10");
    }

    [TestCaseSource(nameof(DepositRefusals))]
    public void Deposit_ThatCannotBe_IsRefusedWithTheFirstReason(
        long coins,
        float distance,
        string npc,
        string row,
        uint quantity,
        CommandRejectionReason expected)
    {
        Counter counter = Enter(coins, distance, npc, store =>
        {
            store.GiveItems(1, Gel, 1, 12, 5);
            store.GiveItems(1, Sword, 1, 1, 6);
            store.Wear(1, store.Stored(1).Items.Single(item => item.ItemDefinitionId == Sword).Id, "Weapon");
        });
        long id = row switch
        {
            "gel" => counter.RowOf(Gel),
            "worn" => counter.RowOf(Sword),
            _ => 999999
        };

        counter.Server.SendStorageDeposit(counter.Player, counter.Npc, id, quantity, 1);

        Assert.That(counter.Answer(), Is.EqualTo(expected));
        Assert.That(counter.Server.Store.Stored(1).Coins, Is.EqualTo(coins), "nothing taken");
        AssertNotScored(counter);
    }

    [Test]
    public void DepositAndWithdraw_ThatTheCommitRefuses_AreFive_ForAFullSide_AndOne_ForARowNotTheAccounts()
    {
        Counter full = Enter(100, prepare: store =>
        {
            for (int index = 0; index < ItemActionSystem.MaxStorageRows; index++)
            {
                store.PutInStorage(1, Sword, 1);
            }

            store.GiveItems(1, Gel, 1, 3, 5);
        });
        long elsewhere = 0;
        Counter other = Enter(0, before: server =>
        {
            ConnectionId second = server.Connect();
            server.SignInWithCharacter(second, 2);
            elsewhere = server.Store.PutInStorage(2, Gel, 5);
        });

        full.Server.SendStorageDeposit(full.Player, full.Npc, full.RowOf(Gel), 3, 1);
        other.Server.SendStorageWithdraw(other.Player, other.Npc, elsewhere, 1, 1);

        Assert.That(
            (full.Answer(), other.Answer()),
            Is.EqualTo((CommandRejectionReason.InventoryFull, CommandRejectionReason.InvalidTarget)));
        Assert.That(full.Server.Store.Stored(1).Coins, Is.EqualTo(100L), "no fee for a deposit that did not happen");
        Assert.That(full.StorageChanges().Concat(other.StorageChanges()), Is.Empty);
    }

    [Test]
    public void Deposit_OfPartOfAStack_TakesTheFee_AndTellsTheBagThenTheStorage()
    {
        Counter counter = Enter(100, prepare: Holding(Gel, 12));
        long gel = counter.RowOf(Gel);
        int ledger = counter.Server.Store.LedgerCount;

        counter.Server.SendStorageDeposit(counter.Player, counter.Npc, gel, 10, 1);

        Assert.That(counter.Answer(), Is.EqualTo(CommandRejectionReason.None));
        InventoryChanged bag = counter.Changes().Single();
        StorageChanged storage = counter.StorageChanges().Single();
        Assert.That(
            (bag.Coins, bag.Changes.Single().InventoryItem, bag.Changes.Single().Quantity),
            Is.EqualTo((80u, gel, 2u)));
        Assert.That(
            (storage.PriorRevision, storage.NewRevision, storage.Row.Item.Value, storage.Row.Quantity),
            Is.EqualTo((0u, 1u, Gel, 10u)));
        Assert.That(counter.Told(), Is.EqualTo(new[] { MessageOpcode.InventoryChanged, MessageOpcode.StorageChanged }));
        Assert.That(
            (counter.Server.Store.Stored(1).Coins, counter.Server.Store.LedgerCount),
            Is.EqualTo((80L, ledger + 1)));
        Assert.That(
            counter.Server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "StorageDeposited")
                .Fields["Coins"],
            Is.EqualTo(20L));
        AssertNotScored(counter);
    }

    [Test]
    public void Deposit_WhoseAnswerIsLost_IsSettledFromTheLedgerOnce_AndAnotherAskedMeanwhileIsNine()
    {
        Counter counter = Enter(100, prepare: Holding(Gel, 12));
        long gel = counter.RowOf(Gel);
        counter.Server.Store.AmbiguousStorageFailures = 100;

        counter.Server.SendStorageDeposit(counter.Player, counter.Npc, gel, 2, 1);
        counter.Server.SendStorageDeposit(counter.Player, counter.Npc, gel, 2, 2);
        counter.Server.Tick();
        counter.Server.TickUntil(() => counter.Character.Operation == null);

        Assert.That(
            counter.Server.Transport.ControlSentTo(counter.Player)
                .Where(message => message.Opcode == MessageOpcode.CommandRejected)
                .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read : default)
                .Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[] { (2u, CommandRejectionReason.ItemActionInFlight) }));
        Assert.That(counter.Server.Store.Stored(1).Coins, Is.EqualTo(80L), "charged once");
        Assert.That(counter.StorageChanges().Single().Row.Quantity, Is.EqualTo(2u));
        Assert.That(
            counter.Server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "InventoryOperationUnsettled")
                .Fields["Kind"],
            Is.EqualTo(InventoryOperationKind.StorageDeposit));
    }

    [Test]
    public void Open_OfAnAccountThatNeverStored_IsOneEmptyPart_AtRevisionZero_AndTwoAskedTogetherAreReadOnce()
    {
        Counter counter = Enter(0);

        counter.Server.SendStorageOpen(counter.Player, counter.Npc, 1);
        counter.Server.SendStorageOpen(counter.Player, counter.Npc, 2);

        Assert.That(counter.Answer(), Is.EqualTo(CommandRejectionReason.None));
        Assert.That(
            counter.Parts().Select(part => (part.PartCount, part.Revision, part.Entries.Count)),
            Is.EqualTo(new[] { ((byte)1, 0u, 0) }),
            "the second was answered by the first's read");
    }

    [Test]
    public void Open_SendsEveryRowInPartsOfTwelve_WithTheFee()
    {
        Counter counter = Enter(0, prepare: store =>
        {
            for (int index = 0; index < 13; index++)
            {
                store.PutInStorage(1, Sword, 1);
            }

            store.PutInStorage(1, Gel, 30);
        });

        counter.Server.SendStorageOpen(counter.Player, counter.Npc, 1);

        Assert.That(counter.Answer(), Is.EqualTo(CommandRejectionReason.None));
        List<StorageSnapshot> parts = counter.Parts();
        Assert.That(
            parts.Select(part => (part.Part, part.PartCount, part.Revision, part.DepositFee, part.Entries.Count)),
            Is.EqualTo(new[] { ((byte)0, (byte)2, 14u, 20u, 12), ((byte)1, (byte)2, 14u, 20u, 2) }));
        Assert.That(
            parts.SelectMany(part => part.Entries).Last(),
            Has.Property(nameof(StorageEntry.Item)).EqualTo(new ItemDefinitionId(Gel))
                .And.Property(nameof(StorageEntry.Quantity)).EqualTo(30u));
        AssertNotScored(counter);
    }

    [Test]
    public void Open_WhileTheDatabaseIsDown_OrOfAnItemTheContentLacks_IsRefusedWithSix_AndTheLatterLogged()
    {
        Counter down = Enter(0);
        Counter unknown = Enter(0, prepare: store => store.PutInStorage(1, "item.material.retired", 1));
        down.Server.Store.IsUnavailable = true;

        down.Server.SendStorageOpen(down.Player, down.Npc, 1);
        unknown.Server.SendStorageOpen(unknown.Player, unknown.Npc, 1);
        down.Server.Tick(3);

        Assert.That(
            (down.Answer(), unknown.Answer()),
            Is.EqualTo((CommandRejectionReason.ServiceUnavailable, CommandRejectionReason.ServiceUnavailable)));
        Assert.That(down.Parts().Concat(unknown.Parts()), Is.Empty);
        Assert.That(
            unknown.Server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "StorageContentMismatch")
                .Fields["Definition"],
            Is.EqualTo("item.material.retired"));
    }

    // Everything unworn may be stored, the Monarch Mantle included (Gameplay Systems §11.4).
    [Test]
    public void Withdraw_OfPartOfARow_IsFree_AndAMonarchMantleIsStoredLikeAnyUnwornRow()
    {
        long stored = 0;
        Counter counter = Enter(25, prepare: store =>
        {
            stored = store.PutInStorage(1, Gel, 10);
            store.GiveItems(1, Mantle, 1, 1, 5);
        });
        long mantle = counter.RowOf(Mantle);

        counter.Server.SendStorageWithdraw(counter.Player, counter.Npc, stored, 4, 1);
        CommandRejectionReason withdrawn = counter.Answer();
        counter.Server.SendStorageDeposit(counter.Player, counter.Npc, mantle, 1, 2);
        CommandRejectionReason deposited = counter.Answer();

        Assert.That((withdrawn, deposited), Is.EqualTo((CommandRejectionReason.None, CommandRejectionReason.None)));
        Assert.That(
            counter.Changes().Select(change =>
                (change.Coins, change.Changes.Single().Item.Value, change.Changes.Single().Quantity)),
            Is.EqualTo(new[] { (25u, Gel, 4u), (5u, Mantle, 0u) }));
        Assert.That(
            counter.StorageChanges().Select(change =>
                (change.PriorRevision, change.NewRevision, change.Row.Item.Value, change.Row.Quantity)),
            Is.EqualTo(new[] { (1u, 2u, Gel, 6u), (2u, 3u, Mantle, 1u) }));
        Assert.That(counter.StorageChanges()[0].Row.StorageItem, Is.EqualTo(stored));
    }
}
}
