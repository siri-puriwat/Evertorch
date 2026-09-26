using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Buying and selling in the store (Persistence §5; the vectors of the NPC and shop research note §4): coins and
///     items move together in one transaction with one ledger row, once under repeats, concurrency, and lost answers,
///     and never past the stack limit, the row limit, or the coin cap.
/// </summary>
[TestFixture]
public sealed class ShopStoreTests
{
    private const string Potion = "item.consumable.minor_health";
    private const string Gel = "item.material.slime_gel";
    private const string Shell = "item.material.crawler_shell";
    private const string Sword = "item.weapon.training_sword";
    private const int PotionPrice = 20;
    private const int PotionStack = 50;
    private const int MaxRows = 100;
    private const long Cap = 1_000_000_000;

    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

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

    private long NewCharacter(long coins)
    {
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Shop"));
        m_sql.Execute($"UPDATE characters SET currency = {coins} WHERE id = {character}");
        return character;
    }

    private long Give(long character, string item, int quantity)
    {
        return m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, '{item}', {quantity}, 0, 0) RETURNING id");
    }

    private void GiveRows(long character, int rows)
    {
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {character}, '{Shell}', 1, 0, 0 FROM generate_series(1, {rows})");
    }

    private InventoryResult Buy(long character, string item, int quantity, int price, int stack, Guid? operation = null)
    {
        return BuyAsync(character, item, quantity, price, stack, operation ?? Guid.NewGuid()).GetAwaiter().GetResult();
    }

    private Task<InventoryResult> BuyAsync(
        long character,
        string item,
        int quantity,
        int price,
        int stack,
        Guid operation)
    {
        return m_store.CommitBuyAsync(
            new BuyCommit(operation, character, item, quantity, price, stack, MaxRows, Now),
            CancellationToken.None);
    }

    private InventoryResult Sell(long character, long row, int quantity, int price, Guid? operation = null)
    {
        return m_store.CommitSellAsync(
                new SellCommit(operation ?? Guid.NewGuid(), character, row, quantity, price, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private long Coins(long character)
    {
        return m_sql.Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    private long Revision(long character)
    {
        return m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {character}");
    }

    private long Rows(long character)
    {
        return m_sql.Scalar($"SELECT count(*) FROM inventory_items WHERE character_id = {character}");
    }

    private string Ledger(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(operation_type || ' ' || quantity_delta || ' ' || currency_delta, ', ' "
            + $"ORDER BY id), '') FROM economy_ledger WHERE actor_character_id = {character}");
    }

    [TestCase(50, InventoryStatus.Refused, 50L, "short of coins")]
    [TestCase(60, InventoryStatus.Committed, 0L, "exactly enough")]
    public void Buy_ForItsWholeCost_NeedsTheCoins(int coins, InventoryStatus expected, long left, string reason)
    {
        long character = NewCharacter(coins);

        InventoryResult bought = Buy(character, Potion, 3, PotionPrice, PotionStack);

        Assert.That((bought.Status, bought.Coins, Coins(character)), Is.EqualTo((expected, left, left)), reason);
        Assert.That(Ledger(character), Is.EqualTo(expected == InventoryStatus.Committed ? "buy 3 -60" : string.Empty));
    }

    [Test]
    public void Buy_IntoANewRow_TakesTheCoins_RaisesTheRevision_AndRecordsIt()
    {
        long character = NewCharacter(100);

        InventoryResult bought = Buy(character, Potion, 1, PotionPrice, PotionStack);

        StoredItem row = bought.Rows.Single();
        Assert.That(
            (bought.Status, bought.Coins, bought.InventoryRevision),
            Is.EqualTo((InventoryStatus.Committed, 80L, 1u)));
        Assert.That((row.ItemDefinitionId, row.Quantity), Is.EqualTo((Potion, 1)));
        Assert.That((Coins(character), Revision(character)), Is.EqualTo((80L, 1L)));
        Assert.That(Ledger(character), Is.EqualTo("buy 1 -20"));
    }

    [Test]
    public void Buy_IntoAStack_MergesIntoItsRow()
    {
        long character = NewCharacter(100);
        long potions = Give(character, Potion, 3);

        InventoryResult bought = Buy(character, Potion, 2, PotionPrice, PotionStack);

        Assert.That(bought.Rows.Select(row => (row.Id, row.Quantity)), Is.EqualTo(new[] { (potions, 5) }));
        Assert.That(bought.Coins, Is.EqualTo(60));
    }

    [Test]
    public void Buy_OfAnItemWithAStackLimitOfOne_TakesARowOfItsOwn_UntilTheRowsRunOut()
    {
        long character = NewCharacter(100);
        GiveRows(character, MaxRows - 1);

        InventoryResult first = Buy(character, Sword, 1, 50, 1);
        InventoryResult second = Buy(character, Sword, 1, 50, 1);

        Assert.That((first.Status, first.Coins), Is.EqualTo((InventoryStatus.Committed, 50L)));
        Assert.That((second.Status, Coins(character)), Is.EqualTo((InventoryStatus.InventoryFull, 50L)));
        Assert.That(Rows(character), Is.EqualTo(MaxRows));
    }

    [Test]
    public void Buy_PastTheStackLimit_OrWithNoFreeRow_IsFull_WhileMergingAtTheRowLimitWorks()
    {
        long atTheLimit = NewCharacter(100);
        Give(atTheLimit, Potion, 49);
        long noRow = NewCharacter(100);
        GiveRows(noRow, MaxRows);
        long merging = NewCharacter(100);
        GiveRows(merging, MaxRows - 1);
        long held = Give(merging, Potion, 10);

        InventoryResult pastTheLimit = Buy(atTheLimit, Potion, 2, PotionPrice, PotionStack);
        InventoryResult withoutARow = Buy(noRow, Potion, 1, PotionPrice, PotionStack);
        InventoryResult merged = Buy(merging, Potion, 1, PotionPrice, PotionStack);

        Assert.That((pastTheLimit.Status, Coins(atTheLimit)), Is.EqualTo((InventoryStatus.InventoryFull, 100L)));
        Assert.That((withoutARow.Status, Coins(noRow)), Is.EqualTo((InventoryStatus.InventoryFull, 100L)));
        Assert.That(merged.Rows.Select(row => (row.Id, row.Quantity)), Is.EqualTo(new[] { (held, 11) }));
        Assert.That(Rows(merging), Is.EqualTo(MaxRows));
    }

    [Test]
    public void Buys_AtOnce_ForTheCoinsOfOne_CommitOne_AndRefuseTheOther()
    {
        long character = NewCharacter(60);

        InventoryResult[] results = Task.WhenAll(
                BuyAsync(character, Potion, 3, PotionPrice, PotionStack, Guid.NewGuid()),
                BuyAsync(character, Potion, 3, PotionPrice, PotionStack, Guid.NewGuid()))
            .GetAwaiter()
            .GetResult();

        Assert.That(
            results.Select(result => result.Status),
            Is.EquivalentTo(new[] { InventoryStatus.Committed, InventoryStatus.Refused }));
        Assert.That(Coins(character), Is.Zero);
        Assert.That(Ledger(character), Is.EqualTo("buy 3 -60"));
    }

    [Test]
    public void Repeats_OfOneOperation_ChargeOnce_AndTheLookupFindsIt()
    {
        long character = NewCharacter(100);
        var operation = Guid.NewGuid();

        Buy(character, Potion, 1, PotionPrice, PotionStack, operation);
        InventoryResult repeat = Buy(character, Potion, 1, PotionPrice, PotionStack, operation);
        InventoryResult? found = m_store
            .FindOperationAsync(operation, character, Array.Empty<long>(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That((repeat.Status, repeat.Coins, Coins(character)), Is.EqualTo((InventoryStatus.Committed, 80L, 80L)));
        Assert.That(repeat.Rows.Single().Quantity, Is.EqualTo(1));
        Assert.That(
            (found!.Status, found.Coins, found.Rows.Single().Quantity),
            Is.EqualTo((InventoryStatus.Committed, 80L, 1)));
        Assert.That(Ledger(character), Is.EqualTo("buy 1 -20"));
    }

    [Test]
    public void Sell_OfMoreThanHeld_AnotherCharactersRow_OrAWornRow_ChangesNothing()
    {
        long character = NewCharacter(0);
        long other = NewCharacter(0);
        long gel = Give(character, Gel, 12);
        long theirs = Give(other, Gel, 12);
        long sword = Give(character, Sword, 1);
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", sword));

        InventoryResult[] refused =
        {
            Sell(character, gel, 13, 2),
            Sell(character, theirs, 1, 2),
            Sell(character, sword, 1, 25)
        };
        m_sql.Execute($"DELETE FROM equipment WHERE character_id = {character}");
        InventoryResult unequipped = Sell(character, sword, 1, 25);

        Assert.That(refused.Select(result => result.Status), Is.All.EqualTo(InventoryStatus.Refused));
        Assert.That((unequipped.Status, unequipped.Coins), Is.EqualTo((InventoryStatus.Committed, 25L)));
        Assert.That(Ledger(character), Is.EqualTo("sell -1 25"));
    }

    [Test]
    public void Sell_OfPartOfAStack_OrAllOfIt_AddsWhatItFetches_AndAnEmptiedRowComesBackAtZero()
    {
        long character = NewCharacter(0);
        long gel = Give(character, Gel, 12);
        long more = Give(character, Shell, 12);

        InventoryResult part = Sell(character, gel, 10, 2);
        InventoryResult whole = Sell(character, more, 12, 2);

        Assert.That(part.Rows.Select(row => (row.Id, row.Quantity)), Is.EqualTo(new[] { (gel, 2) }));
        Assert.That(part.Coins, Is.EqualTo(20));
        Assert.That(
            whole.Rows.Select(row => (row.Id, row.ItemDefinitionId, row.Quantity)),
            Is.EqualTo(new[] { (more, Shell, 0) }));
        Assert.That((whole.Coins, Rows(character)), Is.EqualTo((44L, 1L)));
        Assert.That(Ledger(character), Is.EqualTo("sell -10 20, sell -12 24"));
    }

    [Test]
    public void Sell_ThatWouldPassTheCap_IsRefused_AndOneThatReachesItIsNot()
    {
        long character = NewCharacter(Cap - 10);
        long shells = Give(character, Shell, 5);

        InventoryResult tooMuch = Sell(character, shells, 5, 5);
        InventoryResult exactly = Sell(character, shells, 2, 5);

        Assert.That((tooMuch.Status, tooMuch.Coins), Is.EqualTo((InventoryStatus.Refused, Cap - 10)));
        Assert.That((exactly.Status, Coins(character)), Is.EqualTo((InventoryStatus.Committed, Cap)));
    }
}
}
