using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     An insertion and a removal commit like a refine (Persistence §5): one transaction under the character's lock
///     that changes the card's row and the item's cards, writes one ledger row and one <c>card_changes</c> row, and
///     raises the revision; a repeat answers from the ledger. A refine that destroys a carded row returns its cards, and
///     a sale of one names them in its ledger row.
/// </summary>
[TestFixture]
public sealed class CardStoreTests
{
    private const string Sword = "item.weapon.iron_sword";
    private const string Ore = "item.material.emberstone";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";
    private const int CardStack = 999;
    private const int MaxRows = 100;
    private const long Coins = 1000;
    private const long Fee = 200;

    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<string, int> CardLimits =
        new Dictionary<string, int> { [Keen] = CardStack, [Bane] = CardStack };

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

    private long NewCharacter()
    {
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Card"));
        m_sql.Execute($"UPDATE characters SET currency = {Coins} WHERE id = {character}");
        return character;
    }

    private long Give(long character, string item, int quantity, params string[] cards)
    {
        string data = cards.Length == 0
            ? "NULL"
            : $"'{{\"cards\": [{string.Join(", ", cards.Select(card => $"\"{card}\""))}]}}'::jsonb";
        return m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"instance_data_json, version) VALUES ({character}, '{item}', {quantity}, 0, {data}, 0) RETURNING id");
    }

    private InventoryResult Insert(long character, long card, long sword, int slots = 2, Guid? operation = null,
        string cardItem = Keen)
    {
        return m_store.CommitInsertCardAsync(
                new InsertCardCommit(operation ?? Guid.NewGuid(), character, card, cardItem, sword, Sword, slots, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult Remove(long character, long sword, int slot, string card, Guid? operation = null,
        int maxRows = MaxRows)
    {
        return m_store.CommitRemoveCardAsync(
                new RemoveCardCommit(operation ?? Guid.NewGuid(), character, sword, Sword, slot, card, Fee,
                    CardStack, maxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult Destroy(long character, long sword, long ore, Guid? operation = null,
        int maxRows = MaxRows, IReadOnlyDictionary<string, int>? limits = null)
    {
        return m_store.CommitRefineAsync(
                new RefineCommit(operation ?? Guid.NewGuid(), character, sword, Sword, 0, RefineOutcomeKind.Destroyed,
                    0, ore, Ore, 1, 100, Now, limits ?? CardLimits, maxRows),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private string CardsOf(long row)
    {
        return m_sql.Text(
            "SELECT coalesce(max(coalesce(instance_data_json::text, 'none')), 'gone') FROM inventory_items "
            + $"WHERE id = {row}");
    }

    private string RowOf(long row)
    {
        return m_sql.Text(
            "SELECT coalesce(max(item_definition_id || ' x ' || quantity), 'gone') FROM inventory_items "
            + $"WHERE id = {row}");
    }

    private string Ledger(Guid operation)
    {
        return m_sql.Text(
            "SELECT operation_type || ' ' || item_instance_id || ' ' || item_definition_id || ' ' || quantity_delta "
            + $"|| ' ' || currency_delta FROM economy_ledger WHERE operation_id = '{operation}'");
    }

    private string Changes(Guid operation)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(change || ' ' || card_definition_id || ' ' || item_instance_id || ' ' || slot "
            + "|| ' ' || card_instance_id || ' ' || fee, '; ' ORDER BY id), '') FROM card_changes "
            + $"WHERE operation_id = '{operation}'");
    }

    private long CoinsOf(long character)
    {
        return m_sql.Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    private long Revision(long character)
    {
        return m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {character}");
    }

    private static (long, string, int, string)[] Rows(InventoryResult result)
    {
        return result.Rows
            .Select(row => (row.Id, row.ItemDefinitionId, row.Quantity, string.Join(",", row.Cards)))
            .ToArray();
    }

    [Test]
    public void Insert_Refusals_ChangeNothing()
    {
        long character = NewCharacter();
        long keen = Give(character, Keen, 3);
        long full = Give(character, Sword, 1, Bane, Bane);
        long worn = Give(character, Sword, 1);
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", worn));
        long sword = Give(character, Sword, 1);
        long gel = Give(character, "item.material.slime_gel", 1);
        long others = Give(NewCharacter(), Keen, 1);

        InventoryResult[] results =
        {
            Insert(character, others, sword),
            Insert(character, gel, sword),
            Insert(character, keen, gel),
            Insert(character, keen, keen),
            Insert(character, keen, worn),
            Insert(character, keen, full)
        };

        Assert.That(
            results.Select(result => result.Status),
            Is.EqualTo(new[]
            {
                InventoryStatus.Refused, InventoryStatus.Refused, InventoryStatus.Refused, InventoryStatus.Refused,
                InventoryStatus.RowWorn, InventoryStatus.NoFreeSlot
            }),
            "another's card, a row holding another item, the item's another item, one row, worn, and full");
        Assert.That((RowOf(keen), CardsOf(sword), Revision(character)), Is.EqualTo(($"{Keen} x 3", "none", 0L)));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM card_changes WHERE character_id = {character}"), Is.Zero);
    }

    [Test]
    public void Insert_Repeated_CommitsOnce_AndTheLookupNamesBothRows()
    {
        long character = NewCharacter();
        long keen = Give(character, Keen, 3);
        long sword = Give(character, Sword, 1);
        var operation = Guid.NewGuid();

        Insert(character, keen, sword, operation: operation);
        InventoryResult again = Insert(character, keen, sword, operation: operation);
        InventoryResult? found = m_store.FindOperationAsync(
                operation,
                character,
                new[] { new ReportedRow(sword, Sword) },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(Rows(again), Is.EqualTo(new[] { (keen, Keen, 2, ""), (sword, Sword, 1, Keen) }));
        Assert.That(Rows(found!), Is.EqualTo(Rows(again)));
        Assert.That((RowOf(keen), Revision(character)), Is.EqualTo(($"{Keen} x 2", 1L)), "once");
    }

    [Test]
    public void Insert_TakesOneCard_AndAppendsItToTheItemsCards_SlotBySlot()
    {
        long character = NewCharacter();
        long keen = Give(character, Keen, 3);
        long bane = Give(character, Bane, 1);
        long sword = Give(character, Sword, 1);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        InventoryResult once = Insert(character, keen, sword, operation: first);
        InventoryResult twice = Insert(character, bane, sword, operation: second, cardItem: Bane);

        Assert.That((once.Status, Rows(once)),
            Is.EqualTo((InventoryStatus.Committed, new[] { (keen, Keen, 2, ""), (sword, Sword, 1, Keen) })));
        Assert.That(Rows(twice), Is.EqualTo(new[] { (bane, Bane, 0, ""), (sword, Sword, 1, $"{Keen},{Bane}") }),
            "the last card's row is deleted and answered at 0 under its own item");
        Assert.That(CardsOf(sword), Is.EqualTo($"{{\"cards\": [\"{Keen}\", \"{Bane}\"]}}"));
        Assert.That((RowOf(keen), RowOf(bane)), Is.EqualTo(($"{Keen} x 2", "gone")));
        Assert.That(Ledger(first), Is.EqualTo($"card_insert {keen} {Keen} -1 0"));
        Assert.That(Changes(second), Is.EqualTo($"insert {Bane} {sword} 2 {bane} 0"));
        Assert.That((Revision(character), CoinsOf(character)), Is.EqualTo((2L, Coins)), "free");
    }

    [Test]
    public void Refine_ThatDestroysACardedRow_ReturnsItsCards_AndTheLookupNamesTheirRows()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, Keen, Bane);
        long ore = Give(character, Ore, 1);
        long stack = Give(character, Keen, 4);
        var operation = Guid.NewGuid();

        InventoryResult result = Destroy(character, sword, ore, operation);

        long bane = result.Rows.Single(row => row.ItemDefinitionId == Bane).Id;
        Assert.That(result.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Rows(result),
            Is.EquivalentTo(new[]
                { (sword, Sword, 0, ""), (ore, Ore, 0, ""), (stack, Keen, 5, ""), (bane, Bane, 1, "") }));
        Assert.That(Ledger(operation), Is.EqualTo($"refine {sword} {Sword} -1 -100"), "the refine's one ledger row");
        Assert.That(Changes(operation),
            Is.EqualTo($"return {Keen} {sword} 1 {stack} 0; return {Bane} {sword} 2 {bane} 0"));
        InventoryResult? found = m_store.FindOperationAsync(
                operation,
                character,
                new[] { new ReportedRow(ore, Ore) },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(Rows(found!), Is.EquivalentTo(Rows(result)), "the lookup names the returned rows");
    }

    [Test]
    public void Refine_ThatWouldDestroyACardedRow_WithoutRoomOrAKnownCard_ChangesNothing()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, Keen, Bane);
        long ore = Give(character, Ore, 2);

        InventoryResult full = Destroy(character, sword, ore, maxRows: 2);
        InventoryResult unknown = Destroy(character, sword, ore,
            limits: new Dictionary<string, int> { [Keen] = CardStack });

        Assert.That((full.Status, unknown.Status),
            Is.EqualTo((InventoryStatus.InventoryFull, InventoryStatus.Refused)));
        Assert.That((RowOf(sword), RowOf(ore), CoinsOf(character), Revision(character)),
            Is.EqualTo(($"{Sword} x 1", $"{Ore} x 2", Coins, 0L)));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM refine_attempts WHERE character_id = {character}"), Is.Zero);
    }

    [Test]
    public void Remove_OfTheLastCard_LeavesNoInstanceData_AndTakesARowOfItsOwn()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, Bane);

        InventoryResult result = Remove(character, sword, 1, Bane);

        long card = result.Rows[1].Id;
        Assert.That(Rows(result), Is.EqualTo(new[] { (sword, Sword, 1, ""), (card, Bane, 1, "") }));
        Assert.That((CardsOf(sword), RowOf(card)), Is.EqualTo(("none", $"{Bane} x 1")));
    }

    [Test]
    public void Remove_Refusals_ChangeNothing()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, Keen);
        long worn = Give(character, Sword, 1, Keen);
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", worn));
        long poor = NewCharacter();
        m_sql.Execute($"UPDATE characters SET currency = {Fee - 1} WHERE id = {poor}");
        long poorSword = Give(poor, Sword, 1, Keen);

        InventoryResult[] results =
        {
            Remove(character, sword, 1, Bane),
            Remove(character, sword, 2, Keen),
            Remove(character, worn, 1, Keen),
            Remove(poor, poorSword, 1, Keen),
            Remove(character, sword, 1, Keen, maxRows: 2)
        };

        Assert.That(
            results.Select(result => result.Status),
            Is.EqualTo(new[]
            {
                InventoryStatus.Refused, InventoryStatus.Refused, InventoryStatus.RowWorn,
                InventoryStatus.NotEnoughCoins, InventoryStatus.InventoryFull
            }),
            "another card at the slot, an empty slot, worn, the fee, and a full bag");
        Assert.That((CardsOf(sword), CoinsOf(character), CoinsOf(poor), Revision(character)),
            Is.EqualTo(($"{{\"cards\": [\"{Keen}\"]}}", Coins, Fee - 1, 0L)));
    }

    [Test]
    public void Remove_TakesTheFee_MovesTheLaterCardsDown_AndMergesTheCardIntoItsStack()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, Keen, Bane, Keen);
        long stack = Give(character, Keen, 2);
        var operation = Guid.NewGuid();

        InventoryResult result = Remove(character, sword, 1, Keen, operation);

        Assert.That((result.Status, result.Coins), Is.EqualTo((InventoryStatus.Committed, Coins - Fee)));
        Assert.That(Rows(result), Is.EqualTo(new[] { (sword, Sword, 1, $"{Bane},{Keen}"), (stack, Keen, 3, "") }));
        Assert.That(Ledger(operation), Is.EqualTo($"card_remove {stack} {Keen} 1 -{Fee}"));
        Assert.That(Changes(operation), Is.EqualTo($"remove {Keen} {sword} 1 {stack} {Fee}"));
    }

    [Test]
    public void Sell_OfACardedRow_NamesTheLostCardsInItsLedgerRow()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, Keen, Bane);
        var operation = Guid.NewGuid();

        InventoryResult result = m_store.CommitSellAsync(
                new SellCommit(operation, character, sword, 1, 60, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(
            m_sql.Text($"SELECT metadata_json::text FROM economy_ledger WHERE operation_id = '{operation}'"),
            Is.EqualTo($"{{\"cards\": [\"{Keen}\", \"{Bane}\"]}}"));
    }
}
}
