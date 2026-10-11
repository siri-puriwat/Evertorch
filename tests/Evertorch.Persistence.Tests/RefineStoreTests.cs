using System;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     A refine commits like a sale (Persistence §5): one transaction under the character's lock that takes the coins and
///     the ore, applies the outcome the tick thread drew, writes one <c>refine</c> ledger row and one
///     <c>refine_attempts</c> row, and raises the revision; a repeat answers from the ledger. Its answer reports the row
///     refined and the ore's row, each emptied one at quantity 0 under its own item.
/// </summary>
[TestFixture]
public sealed class RefineStoreTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Ore = "item.material.emberstone";
    private const string OtherOre = "item.material.wardstone";
    private const long Coins = 500;
    private const long Cost = 100;

    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

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
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Smith"));
        m_sql.Execute($"UPDATE characters SET currency = {Coins} WHERE id = {character}");
        return character;
    }

    private long Give(long character, string item, int quantity, int refineLevel = 0)
    {
        return m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, '{item}', {quantity}, {refineLevel}, 0) RETURNING id");
    }

    private InventoryResult Refine(
        long character,
        long sword,
        int levelBefore,
        RefineOutcomeKind outcome,
        int levelAfter,
        long ore,
        Guid? operation = null,
        int oreQuantity = 1,
        long cost = Cost,
        string oreItem = Ore)
    {
        return m_store.CommitRefineAsync(
                new RefineCommit(
                    operation ?? Guid.NewGuid(),
                    character,
                    sword,
                    Sword,
                    levelBefore,
                    outcome,
                    levelAfter,
                    ore,
                    oreItem,
                    oreQuantity,
                    cost,
                    Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private long Revision(long character)
    {
        return m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {character}");
    }

    private long CoinsOf(long character)
    {
        return m_sql.Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    private string RowOf(long row)
    {
        return m_sql.Text(
            $"SELECT coalesce(max(quantity || ' +' || refine_level), 'gone') FROM inventory_items WHERE id = {row}");
    }

    private string Ledger(Guid operation)
    {
        return m_sql.Text(
            "SELECT operation_type || ' ' || item_instance_id || ' ' || item_definition_id || ' ' || quantity_delta "
            + $"|| ' ' || currency_delta FROM economy_ledger WHERE operation_id = '{operation}'");
    }

    private string Attempt(Guid operation)
    {
        return m_sql.Text(
            "SELECT outcome || ' ' || level_before || ' ' || coalesce(level_after::text, 'null') || ' ' "
            + "|| material_instance_id || ' ' || material_definition_id || ' ' || material_quantity || ' ' || coins "
            + $"FROM refine_attempts WHERE operation_id = '{operation}'");
    }

    private static (long, string, int, int)[] Rows(InventoryResult result)
    {
        return result.Rows.Select(row => (row.Id, row.ItemDefinitionId, row.Quantity, row.RefineLevel)).ToArray();
    }

    private InventoryResult? Find(long character, Guid operation, long ore)
    {
        return m_store.FindOperationAsync(operation, character, new[] { new ReportedRow(ore, Ore) },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    // What refines never change (Persistence §11): every character's coins and what the refines spent.
    private long CoinsAccountedFor()
    {
        return m_sql.Scalar("SELECT coalesce(sum(currency), 0) FROM characters")
            - m_sql.Scalar(
                "SELECT coalesce(sum(currency_delta), 0) FROM economy_ledger WHERE operation_type = 'refine'");
    }

    [Test]
    public void FindOperation_AfterARefineThatDestroyedTheRowAndSpentTheLastOre_ReportsBothAtZeroUnderTheirItems()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 7);
        long ore = Give(character, Ore, 1);
        var operation = Guid.NewGuid();
        Refine(character, sword, 7, RefineOutcomeKind.Destroyed, 0, ore, operation);

        InventoryResult found = Find(character, operation, ore)!;

        Assert.That(found.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Rows(found), Is.EqualTo(new[] { (sword, Sword, 0, 0), (ore, Ore, 0, 0) }));
        Assert.That(
            (found.InventoryRevision, found.Coins),
            Is.EqualTo(((uint)Revision(character), Coins - Cost)));
    }

    [Test]
    public void FindOperation_OfARefineNeverCommitted_OrAnotherCharacters_AnswersNothingOrTakenByOther()
    {
        long character = NewCharacter();
        long other = NewCharacter();
        long sword = Give(other, Sword, 1, 3);
        long ore = Give(other, Ore, 1);
        var operation = Guid.NewGuid();
        Refine(other, sword, 3, RefineOutcomeKind.Refined, 4, ore, operation);

        Assert.That(Find(character, Guid.NewGuid(), ore), Is.Null);
        Assert.That(Find(character, operation, ore)!.Status, Is.EqualTo(InventoryStatus.TakenByOther));
    }

    [Test]
    public void Refine_OfAWornRow_IsRefusedAsWorn_AndChangesNothing()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 3);
        long ore = Give(character, Ore, 2);
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", sword));

        InventoryResult result = Refine(character, sword, 3, RefineOutcomeKind.Refined, 4, ore);

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.RowWorn));
        Assert.That((RowOf(sword), RowOf(ore), CoinsOf(character)), Is.EqualTo(("1 +3", "2 +0", Coins)));
    }

    [Test]
    public void Refine_OfAnotherCharactersRow_OrOfNoRow_OrAtAnotherLevel_IsRefused_AndChangesNothing()
    {
        long character = NewCharacter();
        long other = NewCharacter();
        long theirs = Give(other, Sword, 1, 3);
        long sword = Give(character, Sword, 1, 3);
        long ore = Give(character, Ore, 2);
        long revision = Revision(character);

        InventoryResult stolen = Refine(character, theirs, 3, RefineOutcomeKind.Refined, 4, ore);
        InventoryResult missing = Refine(character, 999_999_999, 3, RefineOutcomeKind.Refined, 4, ore);
        InventoryResult stale = Refine(character, sword, 2, RefineOutcomeKind.Refined, 3, ore);

        Assert.That(
            new[] { stolen.Status, missing.Status, stale.Status },
            Is.All.EqualTo(InventoryStatus.Refused));
        Assert.That((RowOf(theirs), RowOf(sword), RowOf(ore)), Is.EqualTo(("1 +3", "1 +3", "2 +0")));
        Assert.That((Revision(character), CoinsOf(character)), Is.EqualTo((revision, Coins)));
    }

    [Test]
    public void Refine_RepeatedWithItsOperation_CommitsOnce_AndAnswersFromTheLedger()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 3);
        long ore = Give(character, Ore, 5);
        var operation = Guid.NewGuid();

        InventoryResult first = Refine(character, sword, 3, RefineOutcomeKind.Refined, 4, ore, operation);
        InventoryResult again = Refine(character, sword, 3, RefineOutcomeKind.Refined, 4, ore, operation);

        Assert.That(again.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Rows(again), Is.EqualTo(Rows(first)), "the ledger's answer, the ore reported beside it");
        Assert.That(again.InventoryRevision, Is.EqualTo(first.InventoryRevision));
        Assert.That((RowOf(sword), RowOf(ore), CoinsOf(character)), Is.EqualTo(("1 +4", "4 +0", Coins - Cost)));
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM refine_attempts WHERE character_id = {character}"),
            Is.EqualTo(1));
    }

    [Test]
    public void Refine_ThatDestroys_DeletesTheRow_AndTheLastOre_ReportingEachAtZeroUnderItsOwnItem()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 7);
        long ore = Give(character, Ore, 1);
        var operation = Guid.NewGuid();

        InventoryResult result = Refine(character, sword, 7, RefineOutcomeKind.Destroyed, 0, ore, operation);

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Rows(result), Is.EqualTo(new[] { (sword, Sword, 0, 0), (ore, Ore, 0, 0) }));
        Assert.That((RowOf(sword), RowOf(ore)), Is.EqualTo(("gone", "gone")));
        Assert.That(Ledger(operation), Is.EqualTo($"refine {sword} {Sword} -1 -{Cost}"));
        Assert.That(Attempt(operation), Is.EqualTo($"destroyed 7 null {ore} {Ore} 1 {Cost}"));
    }

    [Test]
    public void Refine_ThatLeavesTheItem_StillTakesTheCoinsAndTheOre()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 8);
        long ore = Give(character, Ore, 3);
        var operation = Guid.NewGuid();

        InventoryResult result = Refine(character, sword, 8, RefineOutcomeKind.Unchanged, 8, ore, operation);

        Assert.That(Rows(result), Is.EqualTo(new[] { (sword, Sword, 1, 8), (ore, Ore, 2, 0) }));
        Assert.That(result.Coins, Is.EqualTo(Coins - Cost));
        Assert.That(Ledger(operation), Is.EqualTo($"refine {sword} {Sword} 0 -{Cost}"));
        Assert.That(Attempt(operation), Is.EqualTo($"unchanged 8 8 {ore} {Ore} 1 {Cost}"));
    }

    [Test]
    public void Refine_ThatLowers_SetsTheLevelItWasGiven()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 8);
        long ore = Give(character, Ore, 3);
        var operation = Guid.NewGuid();

        InventoryResult result = Refine(character, sword, 8, RefineOutcomeKind.Lowered, 6, ore, operation);

        Assert.That(Rows(result), Is.EqualTo(new[] { (sword, Sword, 1, 6), (ore, Ore, 2, 0) }));
        Assert.That(RowOf(sword), Is.EqualTo("1 +6"));
        Assert.That(Attempt(operation), Is.EqualTo($"lowered 8 6 {ore} {Ore} 1 {Cost}"));
    }

    [Test]
    public void Refine_ThatRaises_TakesTheCoinsAndTheOre_AndRaisesTheLevelByOne()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 3);
        long ore = Give(character, Ore, 5);
        long revision = Revision(character);
        var operation = Guid.NewGuid();

        InventoryResult result = Refine(character, sword, 3, RefineOutcomeKind.Refined, 4, ore, operation);

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Rows(result), Is.EqualTo(new[] { (sword, Sword, 1, 4), (ore, Ore, 4, 0) }));
        Assert.That((result.InventoryRevision, result.Coins), Is.EqualTo(((uint)(revision + 1), Coins - Cost)));
        Assert.That((Revision(character), CoinsOf(character)), Is.EqualTo((revision + 1, Coins - Cost)));
        Assert.That(Ledger(operation), Is.EqualTo($"refine {sword} {Sword} 0 -{Cost}"));
        Assert.That(Attempt(operation), Is.EqualTo($"refined 3 4 {ore} {Ore} 1 {Cost}"));
    }

    [Test]
    public void Refine_WithTooFewCoins_IsRefusedAsShortOfCoins_AndChangesNothing()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 3);
        long ore = Give(character, Ore, 2);

        InventoryResult result = Refine(character, sword, 3, RefineOutcomeKind.Refined, 4, ore, cost: Coins + 1);

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.NotEnoughCoins));
        Assert.That((RowOf(sword), RowOf(ore), CoinsOf(character)), Is.EqualTo(("1 +3", "2 +0", Coins)));
    }

    [Test]
    public void Refine_WithTooFewOre_TheWrongOre_OrTheRowItself_IsRefusedAsShortOfOre()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 3);
        long ore = Give(character, Ore, 2);
        long wrong = Give(character, OtherOre, 5);

        InventoryResult few = Refine(character, sword, 3, RefineOutcomeKind.Refined, 4, ore, oreQuantity: 3);
        InventoryResult other = Refine(character, sword, 3, RefineOutcomeKind.Refined, 4, wrong);
        InventoryResult itself = Refine(character, sword, 3, RefineOutcomeKind.Refined, 4, sword, oreItem: Sword);

        Assert.That(
            new[] { few.Status, other.Status, itself.Status },
            Is.All.EqualTo(InventoryStatus.MaterialShort));
        Assert.That((RowOf(sword), RowOf(ore), RowOf(wrong)), Is.EqualTo(("1 +3", "2 +0", "5 +0")));
        Assert.That(CoinsOf(character), Is.EqualTo(Coins));
    }

    [Test]
    public void Refines_RefusedOrNot_KeepTheCoinsAccountedFor()
    {
        long character = NewCharacter();
        long sword = Give(character, Sword, 1, 6);
        long ore = Give(character, Ore, 3);
        long before = CoinsAccountedFor();

        Refine(character, sword, 6, RefineOutcomeKind.Refined, 7, ore);
        Refine(character, sword, 6, RefineOutcomeKind.Refined, 7, ore);
        Refine(character, sword, 7, RefineOutcomeKind.Unchanged, 7, ore, cost: 40);
        Refine(character, sword, 7, RefineOutcomeKind.Destroyed, 0, ore, cost: 60);

        Assert.That(CoinsAccountedFor(), Is.EqualTo(before));
        Assert.That(CoinsOf(character), Is.EqualTo(Coins - Cost - 40 - 60));
        Assert.That(
            m_sql.Scalar($"SELECT coalesce(sum(coins), 0) FROM refine_attempts WHERE character_id = {character}"),
            Is.EqualTo(Cost + 40 + 60),
            "the attempts' coins, the refused one not among them");
    }
}
}
