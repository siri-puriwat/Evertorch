using System;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Loading a character's party (Persistence §7): its leader and every member's name, job, base level, and account,
///     in the order they joined.
/// </summary>
[TestFixture]
public sealed class PartyStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

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

    private (AccountId Account, long Character, string Name) NewCharacter()
    {
        AccountId account = m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
        string name = Sql.UniqueName("Party");
        var character = new NewCharacter(
            name,
            new JobDefinitionId("job.adventurer"),
            new PrimaryStats(5, 5, 5, 5, 5, 5),
            71,
            23,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(1f, 0f, 2f),
            Now);
        CharacterCreation created = m_store.CreateCharacterAsync(account, character, 3, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return (account, created.CharacterId, name);
    }

    private StoredParty? Load(long character)
    {
        return m_store.LoadPartyAsync(character, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Test]
    public void LoadParty_OfACharacterWithoutOne_IsNull()
    {
        (AccountId _, long character, string _) = NewCharacter();

        Assert.That(Load(character), Is.Null);
    }

    [Test]
    public void LoadParty_OfAnyMember_GivesTheLeader_AndEveryMemberInTheOrderTheyJoined()
    {
        (AccountId leaderAccount, long leader, string leaderName) = NewCharacter();
        (AccountId memberAccount, long member, string memberName) = NewCharacter();
        (AccountId _, long later, string laterName) = NewCharacter();
        long party = m_sql.Scalar(Sql.PartyInsert(leader, leader, member));
        m_sql.Execute(Sql.PartyMemberInsert(party, later, 3));
        m_sql.Execute($"UPDATE characters SET base_level = 12, job_definition_id = 'job.vanguard' WHERE id = {member}");

        StoredParty? fromLeader = Load(leader);
        StoredParty? fromLater = Load(later);

        Assert.That(fromLeader, Is.Not.Null);
        Assert.That((fromLeader!.Id, fromLeader.LeaderCharacterId), Is.EqualTo((party, leader)));
        Assert.That(
            fromLeader.Members.Select(entry => (entry.CharacterId, entry.Name, entry.JoinOrder)),
            Is.EqualTo(new[] { (leader, leaderName, 1), (member, memberName, 2), (later, laterName, 3) }));
        StoredPartyMember second = fromLeader.Members[1];
        Assert.That(
            (second.JobDefinitionId, second.BaseLevel, second.Account),
            Is.EqualTo(("job.vanguard", 12, memberAccount)),
            "as last stored");
        Assert.That(fromLeader.Members[0].Account, Is.EqualTo(leaderAccount));
        Assert.That(fromLater!.Id, Is.EqualTo(party), "the same party from any member");
    }
}
}
