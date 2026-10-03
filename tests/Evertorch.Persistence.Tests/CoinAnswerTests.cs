using System;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Loading reads a character's coins, and every inventory commit and lookup answers with them (Persistence §5,
///     §7). The coins are set here with plain SQL, as purchases, sales, and rewards would have left them.
/// </summary>
[TestFixture]
public sealed class CoinAnswerTests
{
    private const long Coins = 1_234_567;
    private const string Gel = "item.material.slime_gel";
    private const string Sword = "item.weapon.training_sword";
    private const string Weapon = "Weapon";
    private const int MaxRows = 100;

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

    private long NewCharacterWithCoins()
    {
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Coin"));
        m_sql.Execute($"UPDATE characters SET currency = {Coins} WHERE id = {character}");
        return character;
    }

    private InventoryResult PickUp(long character, Guid drop, string item, int amount, int stackLimit)
    {
        return m_store.CommitPickupAsync(
                new PickupCommit(drop, character, item, amount, stackLimit, MaxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult? Find(long character, Guid operation)
    {
        return m_store.FindOperationAsync(operation, character, Array.Empty<long>(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult Equip(long character, long row)
    {
        return m_store.CommitEquipAsync(
                new EquipCommit(Guid.NewGuid(), character, row, Weapon, Array.Empty<long>(), Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult Unequip(long character)
    {
        return m_store.CommitUnequipAsync(
                new UnequipCommit(Guid.NewGuid(), character, Weapon, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult Consume(long character, long row)
    {
        return m_store.CommitConsumeAsync(
                new ConsumeCommit(Guid.NewGuid(), character, row, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    [Test]
    public void Equip_Unequip_AndConsume_AnswerWithTheCoins_WhetherCommittedOrRefused()
    {
        long character = NewCharacterWithCoins();
        long sword = PickUp(character, Guid.NewGuid(), Sword, 1, 1).Rows.Single().Id;
        long gel = PickUp(character, Guid.NewGuid(), Gel, 2, 999).Rows.Single().Id;

        InventoryResult[] answers =
        {
            Equip(character, sword), Equip(character, sword), Unequip(character), Unequip(character),
            Consume(character, gel), Consume(character, long.MaxValue)
        };

        Assert.That(
            answers.Select(answer => answer.Status),
            Is.EqualTo(
                new[]
                {
                    InventoryStatus.Committed, InventoryStatus.Refused, InventoryStatus.Committed,
                    InventoryStatus.Refused, InventoryStatus.Committed, InventoryStatus.Refused
                }));
        Assert.That(answers.Select(answer => answer.Coins), Is.All.EqualTo(Coins));
    }

    [Test]
    public void Load_ReadsTheCoins()
    {
        AccountId account = m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
        var created = new NewCharacter(
            Sql.UniqueName("Load"),
            new JobDefinitionId("job.adventurer"),
            new PrimaryStats(5, 5, 5, 5, 5, 5),
            60,
            20,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(0f, 0f, 0f),
            Now);
        long character = m_store.CreateCharacterAsync(account, created, 3, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .CharacterId;
        long atCreation = m_store.LoadCharacterAsync(account, character, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Coins;
        m_sql.Execute($"UPDATE characters SET currency = {Coins} WHERE id = {character}");

        StoredCharacter? loaded = m_store.LoadCharacterAsync(account, character, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(atCreation, Is.Zero, "a new character holds no coins");
        Assert.That(loaded!.Coins, Is.EqualTo(Coins));
    }

    [Test]
    public void Pickup_CommittedOrFull_AndItsLookups_AnswerWithTheCoins()
    {
        long character = NewCharacterWithCoins();
        long other = NewCharacterWithCoins();
        var drop = Guid.NewGuid();

        InventoryResult committed = PickUp(character, drop, Gel, 3, 999);
        InventoryResult full = PickUp(character, Guid.NewGuid(), Gel, 997, 999);
        InventoryResult? found = Find(character, drop);
        InventoryResult? taken = Find(other, drop);

        Assert.That((committed.Status, committed.Coins), Is.EqualTo((InventoryStatus.Committed, Coins)));
        Assert.That((full.Status, full.Coins), Is.EqualTo((InventoryStatus.InventoryFull, Coins)));
        Assert.That((found!.Status, found.Coins), Is.EqualTo((InventoryStatus.Committed, Coins)));
        Assert.That((taken!.Status, taken.Coins), Is.EqualTo((InventoryStatus.TakenByOther, 0L)), "not its coins");
    }
}
}
