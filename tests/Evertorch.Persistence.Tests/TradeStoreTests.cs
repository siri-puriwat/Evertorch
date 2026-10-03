using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     A face-to-face trade committed in the store (Persistence §5; owner decisions 5, 8, 10, and 11 of the
///     pre-Milestone-14 review): both traders' rows and coins in one transaction under both locks, a <c>trades</c> row,
///     and two <c>trade</c> ledger rows for each line, all or nothing; a whole row changes owner keeping its ID, its
///     refine level, and its instance data; a repeat and the lookup answer from the <c>trades</c> row.
/// </summary>
[TestFixture]
public sealed class TradeStoreTests
{
    private const string Gel = "item.material.slime_gel";
    private const string Sword = "item.weapon.iron_sword";
    private const string Shell = "item.material.crawler_shell";
    private const int GelStack = 999;
    private const int MaxRows = 100;

    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<string, int> Limits = new Dictionary<string, int>
    {
        [Gel] = GelStack,
        [Sword] = 1,
        [Shell] = GelStack
    };

    private PostgresFixture m_database = null!;
    private PostgresGameStore m_store = null!;
    private Sql m_sql = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
        m_store = new PostgresGameStore(m_database.ConnectionString);
        m_sql = new Sql(m_database.ConnectionString);
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    // Two characters, the first with the lower ID, each with its coins.
    private (long First, long Second) NewPair(long firstCoins = 0, long secondCoins = 0, bool isOneAccount = false)
    {
        long account = m_sql.InsertAccount();
        long first = m_sql.InsertCharacter(account, Sql.UniqueName("Ann"));
        long second = m_sql.InsertCharacter(isOneAccount ? account : m_sql.InsertAccount(), Sql.UniqueName("Bob"));
        m_sql.Execute($"UPDATE characters SET currency = {firstCoins} WHERE id = {first}");
        m_sql.Execute($"UPDATE characters SET currency = {secondCoins} WHERE id = {second}");
        return (first, second);
    }

    private long Give(long character, string item, int quantity, int refine = 0, string? instance = null)
    {
        string data = instance == null ? "NULL" : $"'{instance}'::jsonb";
        return m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"instance_data_json, version) VALUES ({character}, '{item}', {quantity}, {refine}, {data}, 0) "
            + "RETURNING id");
    }

    private void GiveRows(long character, int rows)
    {
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {character}, '{Sword}', 1, 0, 0 FROM generate_series(1, {rows})");
    }

    private TradeResult Trade(
        Guid trade,
        long first,
        IReadOnlyList<TradeLine> firstLines,
        long firstCoins,
        long second,
        IReadOnlyList<TradeLine> secondLines,
        long secondCoins)
    {
        return m_store.CommitTradeAsync(
                new TradeCommit(
                    trade,
                    new TraderOffer(first, firstLines, firstCoins),
                    new TraderOffer(second, secondLines, secondCoins),
                    Limits,
                    MaxRows,
                    Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static TradeLine[] Lines(params (long Row, int Quantity)[] lines)
    {
        return lines.Select(line => new TradeLine(line.Row, line.Quantity)).ToArray();
    }

    private TradeResult? Find(Guid trade, long first, long second)
    {
        return m_store.FindTradeAsync(trade, first, second, CancellationToken.None).GetAwaiter().GetResult();
    }

    private string Bag(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(item_definition_id || ' ' || quantity, ', ' ORDER BY id), '') "
            + $"FROM inventory_items WHERE character_id = {character}");
    }

    private string Ledger(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(operation_type || ' ' || coalesce(item_definition_id, '-') || ' ' "
            + "|| quantity_delta || ' ' || currency_delta, ', ' ORDER BY id), '') FROM economy_ledger "
            + $"WHERE actor_character_id = {character}");
    }

    private long Coins(long character)
    {
        return m_sql.Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    private long Revision(long character)
    {
        return m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {character}");
    }

    [Test]
    public void OperationIds_AreVersionFive_TheSameForAReplay_AndDistinctForEachCharacterAndLine()
    {
        var trade = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        Guid first = TradeOperationIds.For(trade, 7, 1);

        Assert.That(TradeOperationIds.For(trade, 7, 1), Is.EqualTo(first));
        Assert.That(
            new[]
            {
                TradeOperationIds.For(trade, 8, 1), TradeOperationIds.For(trade, 7, 2),
                TradeOperationIds.For(Guid.NewGuid(), 7, 1)
            },
            Has.None.EqualTo(first));
        string text = first.ToString("D");
        Assert.That((text[14], "89ab".Contains(text[19])), Is.EqualTo(('5', true)), text);
    }

    [Test]
    public void Refusals_NameTheirSide_AndMoveNothing()
    {
        (long ann, long bob) = NewPair(100, 999_999_950);
        long gel = Give(ann, Gel, 10);
        long worn = Give(ann, Sword, 1);
        m_sql.Execute(Sql.EquipmentInsert(ann, "Weapon", worn));
        long full = NewPair().First;
        GiveRows(full, MaxRows);
        long fullGel = Give(ann, Gel, 1);

        TradeResult wornRow = Trade(Guid.NewGuid(), ann, Lines((worn, 1)), 0, bob, Lines(), 0);
        TradeResult shortRow = Trade(Guid.NewGuid(), ann, Lines((gel, 11)), 0, bob, Lines(), 0);
        TradeResult shortCoins = Trade(Guid.NewGuid(), ann, Lines(), 101, bob, Lines(), 0);
        TradeResult notHers = Trade(Guid.NewGuid(), ann, Lines(), 0, bob, Lines((gel, 1)), 0);
        TradeResult pastTheCap = Trade(Guid.NewGuid(), ann, Lines(), 100, bob, Lines(), 0);
        TradeResult noRoom = Trade(Guid.NewGuid(), ann, Lines((fullGel, 1)), 0, full, Lines(), 0);

        Assert.That(
            new[] { wornRow, shortRow, shortCoins, notHers, pastTheCap, noRoom }
                .Select(result => (result.Status, result.RefusedCharacterId)),
            Is.EqualTo(new[]
            {
                (TradeStatus.Refused, (long?)ann),
                (TradeStatus.Refused, ann),
                (TradeStatus.Refused, ann),
                (TradeStatus.Refused, bob),
                (TradeStatus.CoinCapReached, bob),
                (TradeStatus.InventoryFull, full)
            }));
        Assert.That((Ledger(ann), Ledger(bob), Ledger(full)), Is.EqualTo((string.Empty, string.Empty, string.Empty)));
        Assert.That((Coins(ann), Coins(bob), Revision(ann), Revision(bob)), Is.EqualTo((100L, 999_999_950L, 0L, 0L)));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM trades WHERE first_character_id = {ann}"), Is.Zero);
    }

    [Test]
    public void Repeats_OfOneTrade_MoveOnce_AndTheLookupFindsIt()
    {
        (long ann, long bob) = NewPair(0, 40);
        long gel = Give(ann, Gel, 10);
        var trade = Guid.NewGuid();
        TradeResult? before = Find(trade, ann, bob);

        Trade(trade, ann, Lines((gel, 5)), 0, bob, Lines(), 40);
        TradeResult repeat = Trade(trade, ann, Lines((gel, 5)), 0, bob, Lines(), 40);
        TradeResult? found = Find(trade, ann, bob);

        Assert.That(before, Is.Null, "no trades row yet");
        Assert.That(repeat.Status, Is.EqualTo(TradeStatus.Committed));
        Assert.That((Bag(ann), Bag(bob), Coins(ann), Coins(bob)), Is.EqualTo(($"{Gel} 5", $"{Gel} 5", 40L, 0L)));
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {ann}"),
            Is.EqualTo(2));
        Assert.That(
            (found!.Status, found.First.Coins, found.Second.Rows.Single().Quantity, found.First.InventoryRevision),
            Is.EqualTo((TradeStatus.Committed, 40L, 5, 1u)));
    }

    [Test]
    public void Trade_FromABagOfAHundredRows_TakesItsOwnOfferOutFirst()
    {
        (long ann, long bob) = NewPair();
        GiveRows(bob, MaxRows - 1);
        long shell = Give(bob, Shell, 2);
        long sword = Give(ann, Sword, 1);

        TradeResult result = Trade(Guid.NewGuid(), ann, Lines((sword, 1)), 0, bob, Lines((shell, 2)), 0);

        Assert.That(result.Status, Is.EqualTo(TradeStatus.Committed), "Bob's shell leaves before the sword arrives");
    }

    [Test]
    public void Trade_OfARefinedRowWithInstanceData_KeepsThem_AndTheLoadReadsTheLevel()
    {
        (long ann, long bob) = NewPair(isOneAccount: true);
        long sword = Give(ann, Sword, 1, 7, "{\"note\":\"kept\"}");

        TradeResult result = Trade(Guid.NewGuid(), ann, Lines((sword, 1)), 0, bob, Lines(), 0);
        StoredCharacter? loaded = m_store
            .LoadCharacterAsync(
                new AccountId(m_sql.Scalar($"SELECT account_id FROM characters WHERE id = {bob}")),
                bob,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(result.Status, Is.EqualTo(TradeStatus.Committed), "characters of one account may trade");
        Assert.That(
            m_sql.Text(
                $"SELECT refine_level || ' ' || (instance_data_json ->> 'note') FROM inventory_items WHERE id = {sword}"),
            Is.EqualTo("7 kept"));
        Assert.That(
            loaded!.Items.Select(item => (item.Id, item.RefineLevel)),
            Is.EqualTo(new[] { (sword, 7) }));
        Assert.That(result.Second.Rows.Single().RefineLevel, Is.EqualTo(7), "the answer carries the level");
    }

    [Test]
    public void Trade_OfAWholeStack_MergesIntoTheReceiversStack_AndEmptiesTheGiversRow()
    {
        (long ann, long bob) = NewPair();
        long annGel = Give(ann, Gel, 10);
        long bobGel = Give(bob, Gel, 3);

        Trade(Guid.NewGuid(), ann, Lines((annGel, 10)), 0, bob, Lines(), 0);

        Assert.That((Bag(ann), Bag(bob)), Is.EqualTo((string.Empty, $"{Gel} 13")));
        Assert.That(m_sql.Scalar($"SELECT id FROM inventory_items WHERE character_id = {bob}"), Is.EqualTo(bobGel));
    }

    [Test]
    public void Trade_OfItemsAndCoinsBothWays_MovesThem_AndWritesTwoLedgerRowsForEachLine()
    {
        (long ann, long bob) = NewPair(300, 50);
        long gel = Give(ann, Gel, 10);
        long sword = Give(bob, Sword, 1);
        var trade = Guid.NewGuid();

        TradeResult result = Trade(trade, ann, Lines((gel, 4)), 100, bob, Lines((sword, 1)), 0);

        Assert.That((result.Status, result.RefusedCharacterId), Is.EqualTo((TradeStatus.Committed, (long?)null)));
        Assert.That(Bag(ann), Is.EqualTo($"{Gel} 6, {Sword} 1"), "the sword is Ann's");
        Assert.That(Bag(bob), Is.EqualTo($"{Gel} 4"), "four gel are Bob's");
        Assert.That(
            m_sql.Scalar($"SELECT character_id FROM inventory_items WHERE id = {sword}"),
            Is.EqualTo(ann),
            "the sword's row changed owner and kept its ID");
        Assert.That((Coins(ann), Coins(bob)), Is.EqualTo((200L, 150L)));
        Assert.That((Revision(ann), Revision(bob)), Is.EqualTo((1L, 1L)));
        Assert.That(
            Ledger(ann),
            Is.EqualTo($"trade {Gel} -4 0, trade - 0 -100, trade {Sword} 1 0"),
            "Ann's side of each line");
        Assert.That(
            Ledger(bob),
            Is.EqualTo($"trade {Gel} 4 0, trade - 0 100, trade {Sword} -1 0"),
            "Bob's side of each line");
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM trades WHERE id = '{trade}' AND first_character_id = {ann} "
                + $"AND second_character_id = {bob}"),
            Is.EqualTo(1));
        Assert.That(
            m_sql.Scalar(
                $"SELECT count(*) FROM economy_ledger WHERE metadata_json ->> 'trade' = '{trade}' "
                + $"AND operation_id IN ('{TradeOperationIds.For(trade, ann, 1)}', "
                + $"'{TradeOperationIds.For(trade, bob, 1)}', "
                + $"'{TradeOperationIds.For(trade, ann, 3)}', '{TradeOperationIds.For(trade, bob, 3)}')"),
            Is.EqualTo(4),
            "each row's operation ID derived from the trade's");
        Assert.That(
            result.First.Rows.Select(row => (row.ItemDefinitionId, row.Quantity)),
            Is.EqualTo(new[] { (Gel, 6), (Sword, 1) }),
            "the answer holds Ann's whole bag");
        Assert.That(
            (result.First.InventoryRevision, result.First.Coins, result.Second.InventoryRevision, result.Second.Coins),
            Is.EqualTo((1u, 200L, 1u, 150L)));
    }
}
}
