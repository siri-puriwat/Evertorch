using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     A party as stored (Persistence §7): its leader and its members in the order they joined, each with what the
///     roster shows and the account that decides who may join.
/// </summary>
public sealed class StoredParty
{
    public StoredParty(long id, long leaderCharacterId, IReadOnlyList<StoredPartyMember> members)
    {
        Id = id;
        LeaderCharacterId = leaderCharacterId;
        Members = members ?? throw new ArgumentNullException(nameof(members));
    }

    public long Id { get; }

    public long LeaderCharacterId { get; }

    /// <summary>
    ///     Earliest joined first: the order the lead passes in when the leader leaves (Gameplay Systems §14).
    /// </summary>
    public IReadOnlyList<StoredPartyMember> Members { get; }
}
}
