using System;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The in-memory store keeps PostgreSQL's party rules (Persistence §5; <c>PartyChangeTests</c>), so the server's
///     tests see the same statuses, and its switch loses a committed change's answer.
/// </summary>
[TestFixture]
public sealed class InMemoryPartyStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryGameStore m_store = new();

    private AccountId NewAccount()
    {
        return m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
    }

    private long NewCharacter(AccountId? account = null)
    {
        var character = new NewCharacter(
            $"Mem{Guid.NewGuid():N}".Substring(0, 12),
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

    [Test]
    public void AmbiguousPartyFailures_CommitTheChange_ThenLoseItsAnswer()
    {
        long ann = NewCharacter();
        long bob = NewCharacter();
        m_store.AmbiguousPartyFailures = 1;

        Action create = () => Commit(PartyChange.Create(ann, bob, Now));

        Assert.That(create, Throws.InstanceOf<StoreUnavailableException>());
        Assert.That(
            m_store.FindPartyStateAsync(bob, CancellationToken.None).GetAwaiter().GetResult()!.LeaderCharacterId,
            Is.EqualTo(ann),
            "the change took effect");
        Assert.That(Commit(PartyChange.Create(ann, bob, Now)).Status, Is.EqualTo(PartyChangeStatus.Committed));
        Assert.That(m_store.PartyChanges, Has.Count.EqualTo(2));
    }

    [Test]
    public void Changes_FollowTheStoresRules_FromCreationToDisband()
    {
        long ann = NewCharacter();
        long bob = NewCharacter();
        long cora = NewCharacter();
        long dan = NewCharacter();

        PartyChangeResult created = Commit(PartyChange.Create(ann, bob, Now));
        long party = created.Party!.Id;
        PartyChangeStatus mutual = Commit(PartyChange.Create(bob, ann, Now)).Status;
        PartyChangeStatus retried = Commit(PartyChange.Create(ann, bob, Now)).Status;
        PartyChangeResult joined = Commit(PartyChange.Join(party, ann, cora, 3, Now));
        PartyChangeStatus full = Commit(PartyChange.Join(party, ann, dan, 3, Now)).Status;
        PartyChangeStatus notLeader = Commit(PartyChange.Kick(party, bob, cora)).Status;
        PartyChangeResult leaderLeft = Commit(PartyChange.Leave(party, ann));
        PartyChangeStatus toOutsider = Commit(PartyChange.Lead(party, bob, dan)).Status;
        PartyChangeResult kicked = Commit(PartyChange.Kick(party, bob, cora));
        PartyChangeStatus late = Commit(PartyChange.Leave(party, bob)).Status;

        Assert.That((created.Status, Members(created)), Is.EqualTo((PartyChangeStatus.Committed, new[] { ann, bob })));
        Assert.That((mutual, retried), Is.EqualTo((PartyChangeStatus.AlreadyInParty, PartyChangeStatus.Committed)));
        Assert.That(Members(joined), Is.EqualTo(new[] { ann, bob, cora }));
        Assert.That((full, notLeader), Is.EqualTo((PartyChangeStatus.PartyFull, PartyChangeStatus.NotTheLeader)));
        Assert.That(leaderLeft.Party!.LeaderCharacterId, Is.EqualTo(bob), "the earliest joined leads");
        Assert.That(toOutsider, Is.EqualTo(PartyChangeStatus.NotAMember));
        Assert.That((kicked.Status, kicked.Party), Is.EqualTo((PartyChangeStatus.Committed, (StoredParty?)null)),
            "one left: disbanded");
        Assert.That(late, Is.EqualTo(PartyChangeStatus.Committed), "a departure from a party that is gone holds");
        Assert.That(m_store.LoadPartyAsync(bob, CancellationToken.None).GetAwaiter().GetResult(), Is.Null);
    }

    [Test]
    public void Changes_OfOneAccountsCharacters_AreRefused()
    {
        AccountId account = NewAccount();
        long first = NewCharacter(account);
        long second = NewCharacter(account);
        long leader = NewCharacter();
        long party = Commit(PartyChange.Create(leader, first, Now)).Party!.Id;

        Assert.That(Commit(PartyChange.Create(first, second, Now)).Status,
            Is.EqualTo(PartyChangeStatus.AlreadyInParty));
        Assert.That(Commit(PartyChange.Join(party, leader, second, 5, Now)).Status,
            Is.EqualTo(PartyChangeStatus.SameAccount));
        Assert.That(Commit(PartyChange.Join(long.MaxValue, leader, second, 5, Now)).Status,
            Is.EqualTo(PartyChangeStatus.NoSuchParty));
    }
}
}
