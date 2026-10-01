using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     One member as the party list shows it: what the roster says, and its health and SP in thousandths once a
///     status has come.
/// </summary>
public sealed class PartyMember
{
    public PartyMember(PartyRosterEntry entry, bool isLeader)
    {
        Name = entry.Name;
        Job = entry.Job;
        BaseLevel = entry.BaseLevel;
        Map = entry.Map;
        IsLeader = isLeader;
    }

    public string Name { get; }

    public JobDefinitionId Job { get; }

    public int BaseLevel { get; }

    /// <summary>
    ///     Null while the member is not in the world.
    /// </summary>
    public MapDefinitionId? Map { get; }

    public bool IsInWorld => Map != null;

    public bool IsLeader { get; }

    /// <summary>
    ///     Null until a status of the member has come.
    /// </summary>
    public int? HealthPermille { get; internal set; }

    public int? SpiritPermille { get; internal set; }
}

/// <summary>
///     The player's party and the invite waiting for an answer (Prototype Content §2), a pure model kept outside
///     <see cref="ClientWorld" /> so that it outlives a map change and a reconnect, as the chat log does. The roster
///     replaces it whole; each status updates one member.
/// </summary>
public sealed class ClientParty
{
    /// <summary>
    ///     How long an invite waits for its answer, as long as the server keeps it (Gameplay Systems §14).
    /// </summary>
    public const double InviteSeconds = 30.0;

    private readonly List<PartyMember> m_members = new();

    public IReadOnlyList<PartyMember> Members => m_members;

    public bool IsInParty => m_members.Count > 0;

    /// <summary>
    ///     The inviter of the invite waiting for an answer; null when none is.
    /// </summary>
    public string? Inviter { get; private set; }

    /// <summary>
    ///     When, in the clock passed to <see cref="Invite" />, the invite waiting runs out.
    /// </summary>
    public double InviteEndsAt { get; private set; }

    public PartyMember? Leader
    {
        get
        {
            foreach (PartyMember member in m_members)
            {
                if (member.IsLeader)
                {
                    return member;
                }
            }

            return null;
        }
    }

    /// <summary>
    ///     Raised after any change of the members, a status, or the invite.
    /// </summary>
    public event Action? Changed;

    public bool IsLeader(string? name)
    {
        PartyMember? leader = Leader;
        return leader != null && string.Equals(leader.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetMember(string name, out PartyMember? member)
    {
        foreach (PartyMember candidate in m_members)
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

    /// <summary>
    ///     The roster replaces the party whole; a member it still lists keeps the status it had.
    /// </summary>
    public void Apply(PartyRoster roster)
    {
        if (roster == null)
        {
            throw new ArgumentNullException(nameof(roster));
        }

        var statuses = new Dictionary<string, (int? Health, int? Spirit)>(StringComparer.OrdinalIgnoreCase);
        foreach (PartyMember member in m_members)
        {
            statuses[member.Name] = (member.HealthPermille, member.SpiritPermille);
        }

        m_members.Clear();
        for (int index = 0; index < roster.Members.Count; index++)
        {
            var member = new PartyMember(roster.Members[index], index == roster.LeaderIndex);
            if (statuses.TryGetValue(member.Name, out (int? Health, int? Spirit) status))
            {
                member.HealthPermille = status.Health;
                member.SpiritPermille = status.Spirit;
            }

            m_members.Add(member);
        }

        Changed?.Invoke();
    }

    public void Apply(PartyMemberStatus status)
    {
        if (status == null)
        {
            throw new ArgumentNullException(nameof(status));
        }

        if (TryGetMember(status.Name, out PartyMember? member))
        {
            member!.HealthPermille = status.HealthPermille;
            member.SpiritPermille = status.SpiritPermille;
            Changed?.Invoke();
        }
    }

    /// <summary>
    ///     An invite from <paramref name="inviter" /> came at <paramref name="now" />; a newer one replaces it.
    /// </summary>
    public void Invite(string inviter, double now)
    {
        Inviter = inviter ?? throw new ArgumentNullException(nameof(inviter));
        InviteEndsAt = now + InviteSeconds;
        Changed?.Invoke();
    }

    /// <summary>
    ///     Forgets the invite waiting, once it is answered.
    /// </summary>
    public void EndInvite()
    {
        if (Inviter == null)
        {
            return;
        }

        Inviter = null;
        Changed?.Invoke();
    }

    /// <summary>
    ///     Forgets the invite waiting once its time is up at <paramref name="now" />.
    /// </summary>
    public void ExpireInvite(double now)
    {
        if (Inviter != null && now >= InviteEndsAt)
        {
            EndInvite();
        }
    }

    /// <summary>
    ///     Another character takes the client's place in the world: none of this is its.
    /// </summary>
    public void Clear()
    {
        if (m_members.Count == 0 && Inviter == null)
        {
            return;
        }

        m_members.Clear();
        Inviter = null;
        Changed?.Invoke();
    }
}
}
