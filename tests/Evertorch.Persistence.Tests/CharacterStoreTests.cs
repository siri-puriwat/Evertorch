using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Character listing and creation (Persistence §4): the name is unique in any letter case, an account holds at most
///     three characters, and a new character starts at level 1 with what the server chose.
/// </summary>
[TestFixture]
public sealed class CharacterStoreTests
{
    private const int Limit = 3;

    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

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

    private static NewCharacter Character(string name)
    {
        return new NewCharacter(
            name,
            new JobDefinitionId("job.adventurer"),
            new PrimaryStats(5, 6, 7, 8, 9, 10),
            71,
            23,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(1.5f, 0.25f, -2f),
            Now);
    }

    private AccountId NewAccount()
    {
        return m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
    }

    private CharacterCreation Create(AccountId account, string name)
    {
        return m_store.CreateCharacterAsync(account, Character(name), Limit, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private IReadOnlyList<CharacterSummary> List(AccountId account)
    {
        return m_store.ListCharactersAsync(account, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Test]
    public void CreateCharacter_BeyondThreeOnOneAccount_IsRefusedAndListsTheThree()
    {
        AccountId account = NewAccount();
        for (int index = 0; index < Limit; index++)
        {
            Create(account, Sql.UniqueName("Lim"));
        }

        CharacterCreation refused = Create(account, Sql.UniqueName("Lim"));

        Assert.That(refused.Status, Is.EqualTo(CharacterCreationStatus.LimitReached));
        Assert.That(refused.Characters, Has.Count.EqualTo(Limit));
        Assert.That(List(account), Has.Count.EqualTo(Limit));
    }

    [Test]
    public void CreateCharacter_FiveAtOnceOnOneAccount_CreatesExactlyThree()
    {
        AccountId account = NewAccount();

        CharacterCreation[] results = Task.WhenAll(
                Enumerable.Range(0, 5)
                    .Select(_ => Task.Run(() => m_store.CreateCharacterAsync(
                        account,
                        Character(Sql.UniqueName("Race")),
                        Limit,
                        CancellationToken.None))))
            .GetAwaiter()
            .GetResult();

        Assert.That(results.Count(result => result.Status == CharacterCreationStatus.Created), Is.EqualTo(Limit));
        Assert.That(results.Count(result => result.Status == CharacterCreationStatus.LimitReached), Is.EqualTo(2));
        Assert.That(List(account), Has.Count.EqualTo(Limit));
    }

    [Test]
    public void CreateCharacter_OneNameTwiceAtOnce_CreatesItOnce()
    {
        string name = Sql.UniqueName("Twin");

        CharacterCreation[] results = Task.WhenAll(
                Enumerable.Range(0, 2)
                    .Select(_ => Task.Run(() => m_store.CreateCharacterAsync(
                        NewAccount(),
                        Character(name),
                        Limit,
                        CancellationToken.None))))
            .GetAwaiter()
            .GetResult();

        Assert.That(results.Select(result => result.Status), Is.EquivalentTo(
            new[] { CharacterCreationStatus.Created, CharacterCreationStatus.NameTaken }));
    }

    [Test]
    public void CreateCharacter_WithAFreeName_StoresItAtLevelOneWithTheChosenValues()
    {
        AccountId account = NewAccount();
        string name = Sql.UniqueName("Ann");

        CharacterCreation created = Create(account, name);

        Assert.That(created.Status, Is.EqualTo(CharacterCreationStatus.Created));
        Assert.That(created.CharacterId, Is.Positive);
        CharacterSummary summary = created.Characters.Single();
        Assert.That(summary.Id, Is.EqualTo(created.CharacterId));
        Assert.That(summary.Name, Is.EqualTo(name));
        Assert.That(summary.JobDefinitionId, Is.EqualTo("job.adventurer"));
        Assert.That(summary.BaseLevel, Is.EqualTo(1));
        long id = created.CharacterId;
        Assert.That(
            m_sql.Scalar(
                $"SELECT count(*) FROM characters WHERE id = {id} AND job_level = 1 AND base_exp = 0 AND job_exp = 0 "
                + "AND str = 5 AND agi = 6 AND vit = 7 AND \"int\" = 8 AND dex = 9 AND luk = 10 AND hp = 71 AND sp = 23 "
                + "AND currency = 0 AND map_definition_id = 'map.training_ground' AND position_x = 1.5 "
                + "AND position_y = 0.25 AND position_z = -2 AND inventory_revision = 0 "
                + $"AND name_normalized = '{name.ToLowerInvariant()}'"),
            Is.EqualTo(1));
    }

    [Test]
    public void CreateCharacter_WithANameTakenByAnotherAccountInAnotherCase_IsRefused()
    {
        string name = Sql.UniqueName("Dup");
        Create(NewAccount(), name);
        AccountId other = NewAccount();

        CharacterCreation refused = Create(other, name.ToUpperInvariant());

        Assert.That(refused.Status, Is.EqualTo(CharacterCreationStatus.NameTaken));
        Assert.That(refused.CharacterId, Is.Zero);
        Assert.That(refused.Characters, Is.Empty);
    }

    [Test]
    public void ListCharacters_ForANewAccount_IsEmpty()
    {
        Assert.That(List(NewAccount()), Is.Empty);
    }

    [Test]
    public void ListCharacters_ShowsOnlyTheAccountsOwnInCreationOrder()
    {
        AccountId account = NewAccount();
        CharacterCreation first = Create(account, Sql.UniqueName("One"));
        Create(NewAccount(), Sql.UniqueName("Else"));
        CharacterCreation second = Create(account, Sql.UniqueName("Two"));

        IReadOnlyList<CharacterSummary> listed = List(account);

        Assert.That(listed.Select(character => character.Id),
            Is.EqualTo(new[] { first.CharacterId, second.CharacterId }));
    }
}
}
