using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Party changes on PostgreSQL (Persistence §5): each kind commits under the party's lock, every refusal is a
///     status, a change whose end state already holds is committed again, and a lookup waits for a change in flight.
/// </summary>
[TestFixture]
public sealed class PartyChangeTests
{
    private const int MaxMembers = 3;

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

    private AccountId NewAccount()
    {
        return m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
    }

    private long NewCharacter(AccountId? account = null)
    {
        var character = new NewCharacter(
            Sql.UniqueName("Pc"),
            new JobDefinitionId("job.adventurer"),
            new PrimaryStats(5, 5, 5, 5, 5, 5),
            71,
            23,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(1f, 0f, 2f),
            Now);
        return m_store.CreateCharacterAsync(account ?? NewAccount(), character, 3, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .CharacterId;
    }

    private PartyChangeResult Commit(PartyChange change)
    {
        return m_store.CommitPartyChangeAsync(change, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static long[] Members(PartyChangeResult result)
    {
        return result.Party!.Members.Select(member => member.CharacterId).ToArray();
    }

    // A party of the leader and one member.
    private (long Party, long Leader, long Member) NewParty()
    {
        long leader = NewCharacter();
        long member = NewCharacter();
        PartyChangeResult created = Commit(PartyChange.Create(leader, member, Now));
        Assert.That(created.Status, Is.EqualTo(PartyChangeStatus.Committed));
        return (created.Party!.Id, leader, member);
    }

    private static void Execute(NpgsqlConnection connection, string commandText)
    {
        using var command = new NpgsqlCommand(commandText, connection);
        command.ExecuteNonQuery();
    }

    // Polled from its own connection, which sees the activity view afresh each time.
    private bool WaitForALockWait()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            long waiting = m_sql.Scalar(
                "SELECT count(*) FROM pg_stat_activity WHERE cardinality(pg_blocking_pids(pid)) > 0");
            if (waiting > 0)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    [Test]
    public void Create_MakesTheInviterLeader_AndIsCommittedAgainOnARetry()
    {
        long leader = NewCharacter();
        long member = NewCharacter();

        PartyChangeResult first = Commit(PartyChange.Create(leader, member, Now));
        PartyChangeResult retried = Commit(PartyChange.Create(leader, member, Now));

        Assert.That(first.Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That((first.Party!.LeaderCharacterId, Members(first)), Is.EqualTo((leader, new[] { leader, member })));
        Assert.That((retried.Status, retried.Party!.Id), Is.EqualTo((PartyChangeStatus.Committed, first.Party.Id)));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM parties WHERE leader_character_id = {leader}"), Is.EqualTo(1));
    }

    [Test]
    public void Create_OfTwoCharactersOfOneAccount_OrOfOneAlreadyInAParty_IsRefused()
    {
        AccountId account = NewAccount();
        long first = NewCharacter(account);
        long second = NewCharacter(account);
        (long _, long leader, long member) = NewParty();

        Assert.That(Commit(PartyChange.Create(first, second, Now)).Status, Is.EqualTo(PartyChangeStatus.SameAccount));
        Assert.That(Commit(PartyChange.Create(first, member, Now)).Status,
            Is.EqualTo(PartyChangeStatus.AlreadyInParty));
        Assert.That(Commit(PartyChange.Create(leader, first, Now)).Status,
            Is.EqualTo(PartyChangeStatus.AlreadyInParty));
    }

    // Two characters accepting each other's invites: the second creation finds them in a party and is refused with
    // a status, never a key violation.
    [Test]
    public void Create_TheOtherWayRound_AfterTheyFormedAParty_IsAlreadyInParty()
    {
        long ann = NewCharacter();
        long bob = NewCharacter();

        PartyChangeResult first = Commit(PartyChange.Create(ann, bob, Now));
        PartyChangeResult second = Commit(PartyChange.Create(bob, ann, Now));

        Assert.That(first.Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That(second.Status, Is.EqualTo(PartyChangeStatus.AlreadyInParty));
        Assert.That(second.Party!.LeaderCharacterId, Is.EqualTo(ann), "the first creation stands");
    }

    [Test]
    public void FindPartyState_GivesTheStoredParty_OrNothing()
    {
        (long party, long leader, long _) = NewParty();

        StoredParty? found = m_store.FindPartyStateAsync(leader, CancellationToken.None).GetAwaiter().GetResult();
        StoredParty? none = m_store.FindPartyStateAsync(NewCharacter(), CancellationToken.None).GetAwaiter()
            .GetResult();

        Assert.That(found!.Id, Is.EqualTo(party));
        Assert.That(none, Is.Null);
    }

    // A change in flight holds the party's lock: the lookup waits for it and sees it settled.
    [Test]
    public void FindPartyState_WhileAChangeHoldsThePartysLock_WaitsAndSeesItSettled()
    {
        (long party, long leader, long member) = NewParty();
        long third = NewCharacter();
        Commit(PartyChange.Join(party, leader, third, MaxMembers, Now));
        using var holder = new NpgsqlConnection(m_database.ConnectionString);
        holder.Open();
        using NpgsqlTransaction transaction = holder.BeginTransaction();
        Execute(holder, $"SELECT id FROM parties WHERE id = {party} FOR UPDATE");
        Execute(holder, $"DELETE FROM party_members WHERE character_id = {third}");

        Task<StoredParty?> lookup = m_store.FindPartyStateAsync(member, CancellationToken.None);
        bool isWaiting = WaitForALockWait();
        transaction.Commit();
        StoredParty? settled = lookup.GetAwaiter().GetResult();

        Assert.That(isWaiting, Is.True, "the lookup waited on the lock");
        Assert.That(settled!.Members.Select(entry => entry.CharacterId), Is.EqualTo(new[] { leader, member }));
    }

    [Test]
    public void Join_AddsTheInviteeLast_UpToTheLimit_AndIsCommittedAgainOnARetry()
    {
        (long party, long leader, long member) = NewParty();
        long third = NewCharacter();
        long fourth = NewCharacter();

        PartyChangeResult joined = Commit(PartyChange.Join(party, leader, third, MaxMembers, Now));
        PartyChangeResult retried = Commit(PartyChange.Join(party, leader, third, MaxMembers, Now));
        PartyChangeResult full = Commit(PartyChange.Join(party, leader, fourth, MaxMembers, Now));

        Assert.That(joined.Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That(Members(joined), Is.EqualTo(new[] { leader, member, third }));
        Assert.That(joined.Party!.Members.Select(entry => entry.JoinOrder), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(retried.Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That((full.Status, Members(full)), Is.EqualTo((PartyChangeStatus.PartyFull, Members(joined))));
    }

    [Test]
    public void Join_ByAMemberWhoIsNotTheLeader_OfAnotherAccountsCharacter_OrToNoParty_IsRefused()
    {
        (long party, long leader, long member) = NewParty();
        AccountId account = NewAccount();
        long invitee = NewCharacter(account);
        long sibling = NewCharacter(account);
        Commit(PartyChange.Join(party, leader, invitee, 5, Now));
        (long _, long _, long elsewhere) = NewParty();

        Assert.That(
            Commit(PartyChange.Join(party, member, NewCharacter(), 5, Now)).Status,
            Is.EqualTo(PartyChangeStatus.NotTheLeader));
        Assert.That(
            Commit(PartyChange.Join(party, leader, sibling, 5, Now)).Status,
            Is.EqualTo(PartyChangeStatus.SameAccount));
        Assert.That(
            Commit(PartyChange.Join(party, leader, elsewhere, 5, Now)).Status,
            Is.EqualTo(PartyChangeStatus.AlreadyInParty));
        Assert.That(
            Commit(PartyChange.Join(long.MaxValue, leader, NewCharacter(), 5, Now)).Status,
            Is.EqualTo(PartyChangeStatus.NoSuchParty));
    }

    [Test]
    public void Kick_ByTheLeader_RemovesTheMember_AndNobodyElseMayKick()
    {
        (long party, long leader, long member) = NewParty();
        long third = NewCharacter();
        Commit(PartyChange.Join(party, leader, third, MaxMembers, Now));

        PartyChangeStatus byMember = Commit(PartyChange.Kick(party, member, third)).Status;
        PartyChangeStatus ofItself = Commit(PartyChange.Kick(party, leader, leader)).Status;
        PartyChangeResult kicked = Commit(PartyChange.Kick(party, leader, third));
        PartyChangeResult again = Commit(PartyChange.Kick(party, leader, third));

        Assert.That((byMember, ofItself), Is.EqualTo((PartyChangeStatus.NotTheLeader, PartyChangeStatus.NotAMember)));
        Assert.That((kicked.Status, Members(kicked)),
            Is.EqualTo((PartyChangeStatus.Committed, new[] { leader, member })));
        Assert.That(again.Status, Is.EqualTo(PartyChangeStatus.Committed), "already gone");
    }

    [Test]
    public void Lead_PassesTheLeadToAMember_OnlyFromTheLeader()
    {
        (long party, long leader, long member) = NewParty();

        PartyChangeStatus byMember = Commit(PartyChange.Lead(party, member, member)).Status;
        PartyChangeStatus toOutsider = Commit(PartyChange.Lead(party, leader, NewCharacter())).Status;
        PartyChangeResult passed = Commit(PartyChange.Lead(party, leader, member));
        PartyChangeResult retried = Commit(PartyChange.Lead(party, leader, member));

        Assert.That((byMember, toOutsider), Is.EqualTo((PartyChangeStatus.NotTheLeader, PartyChangeStatus.NotAMember)));
        Assert.That((passed.Status, passed.Party!.LeaderCharacterId),
            Is.EqualTo((PartyChangeStatus.Committed, member)));
        Assert.That(retried.Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That(m_sql.Scalar($"SELECT version FROM parties WHERE id = {party}"), Is.EqualTo(1), "one change");
    }

    [Test]
    public void Leave_OfTheLeader_PassesTheLeadToTheEarliestJoined()
    {
        (long party, long leader, long member) = NewParty();
        long third = NewCharacter();
        Commit(PartyChange.Join(party, leader, third, MaxMembers, Now));

        PartyChangeResult left = Commit(PartyChange.Leave(party, leader));

        Assert.That(left.Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That((left.Party!.LeaderCharacterId, Members(left)), Is.EqualTo((member, new[] { member, third })));
    }

    [Test]
    public void Leave_ThatLeavesOneMember_DisbandsTheParty_AndARetryOrALateDepartureIsCommitted()
    {
        (long party, long leader, long member) = NewParty();

        PartyChangeResult left = Commit(PartyChange.Leave(party, member));
        PartyChangeResult retried = Commit(PartyChange.Leave(party, member));
        PartyChangeResult late = Commit(PartyChange.Leave(party, leader));

        Assert.That((left.Status, left.Party), Is.EqualTo((PartyChangeStatus.Committed, (StoredParty?)null)));
        Assert.That(retried.Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That(late.Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM parties WHERE id = {party}"), Is.Zero);
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM party_members WHERE character_id IN ({leader}, {member})"),
            Is.Zero);
    }
}
}
