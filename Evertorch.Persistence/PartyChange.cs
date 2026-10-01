using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One party change for <c>CommitPartyChangeAsync</c>. <see cref="Actor" /> is the character acting: the inviter
///     for a creation or a join, the one leaving, or the leader; <see cref="Target" /> the invitee, the member removed,
///     or the new leader.
/// </summary>
public sealed class PartyChange
{
    private PartyChange(PartyChangeKind kind, long partyId, long actor, long target, int maxMembers, DateTime at)
    {
        Kind = kind;
        PartyId = partyId;
        Actor = actor;
        Target = target;
        MaxMembers = maxMembers;
        At = at;
    }

    public PartyChangeKind Kind { get; }

    /// <summary>
    ///     The party changed; 0 for a creation.
    /// </summary>
    public long PartyId { get; }

    public long Actor { get; }

    public long Target { get; }

    public int MaxMembers { get; }

    public DateTime At { get; }

    public static PartyChange Create(long leader, long invitee, DateTime at)
    {
        return new PartyChange(PartyChangeKind.Create, 0, leader, invitee, 2, at);
    }

    public static PartyChange Join(long partyId, long leader, long invitee, int maxMembers, DateTime at)
    {
        if (maxMembers < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMembers), "A party holds at least two.");
        }

        return new PartyChange(PartyChangeKind.Join, partyId, leader, invitee, maxMembers, at);
    }

    public static PartyChange Leave(long partyId, long member)
    {
        return new PartyChange(PartyChangeKind.Leave, partyId, member, member, 0, default);
    }

    public static PartyChange Kick(long partyId, long leader, long member)
    {
        return new PartyChange(PartyChangeKind.Kick, partyId, leader, member, 0, default);
    }

    public static PartyChange Lead(long partyId, long leader, long newLeader)
    {
        return new PartyChange(PartyChangeKind.Lead, partyId, leader, newLeader, 0, default);
    }
}
}
