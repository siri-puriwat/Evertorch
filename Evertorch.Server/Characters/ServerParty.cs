using System;
using System.Collections.Generic;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     A party as the server holds it while any of its members is in the world or a change of it is in flight: the
///     stored party as of its last committed change (Gameplay Systems §14). <see cref="PartyRegistry" /> alone changes
///     it.
/// </summary>
public sealed class ServerParty
{
    internal ServerParty(StoredParty stored)
    {
        Id = stored.Id;
        Members = stored.Members;
        LeaderCharacterId = stored.LeaderCharacterId;
    }

    public long Id { get; }

    public long LeaderCharacterId { get; private set; }

    /// <summary>
    ///     Earliest joined first, online or not.
    /// </summary>
    public IReadOnlyList<StoredPartyMember> Members { get; private set; }

    /// <summary>
    ///     A change of the party is being committed, or its lost answer settled; no other may start.
    /// </summary>
    public bool IsChanging { get; internal set; }

    public bool HasMember(long characterId)
    {
        foreach (StoredPartyMember member in Members)
        {
            if (member.CharacterId == characterId)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasAccount(AccountId account)
    {
        foreach (StoredPartyMember member in Members)
        {
            if (member.Account == account)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The member named <paramref name="name" />, whatever its case (names are unique by their lower-case form).
    /// </summary>
    public bool TryGetMember(string name, out StoredPartyMember? member)
    {
        foreach (StoredPartyMember candidate in Members)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                member = candidate;
                return true;
            }
        }

        member = null;
        return false;
    }

    internal void Replace(StoredParty stored)
    {
        Members = stored.Members;
        LeaderCharacterId = stored.LeaderCharacterId;
    }
}
}
