using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Tells each party's members about it (Network Protocol §9): the roster in every baseline, with each other
///     member's health and SP, and to every member in the world whenever the roster changes; and a
///     member's health and SP to the others after a change, at most once a second. Registered after
///     <see cref="CharacterSyncPhase" /> in the same phase, so the baseline's party follows the character's own
///     messages.
/// </summary>
public sealed class PartySyncPhase : ITickPhase
{
    private static readonly PartyRoster NoParty = new(0, Array.Empty<PartyRosterEntry>());

    private readonly SessionRegistry m_sessions;
    private readonly PartyRegistry m_parties;
    private readonly MessageSender m_sender;
    private readonly uint m_statusIntervalTicks;
    private readonly List<PartyRosterEntry> m_entries = new();

    public PartySyncPhase(
        SessionRegistry sessions,
        PartyRegistry parties,
        MessageSender sender,
        IOptions<SimulationOptions> simulation)
    {
        m_sessions = sessions;
        m_parties = parties;
        m_sender = sender;
        m_statusIntervalTicks = (uint)simulation.Value.TickRate;
    }

    public TickPhase Phase => TickPhase.FinalizeWorld;

    public void Execute(in TickContext context)
    {
        foreach (ServerParty party in m_parties.Parties)
        {
            PartyRoster roster = RosterOf(party);
            if (!IsSame(roster, party.LastRoster))
            {
                party.LastRoster = roster;
                foreach (StoredPartyMember member in party.Members)
                {
                    if (TryGetListener(member.CharacterId, out ClientSession? listener))
                    {
                        listener!.NeedsPartyRoster = true;
                    }
                }
            }
        }

        foreach (ClientSession session in m_sessions.Sessions)
        {
            CharacterSession? character = session.Character;
            if (session.NeedsPartyRoster && session.State == SessionState.InWorld && character != null)
            {
                session.NeedsPartyRoster = false;
                SendRoster(session, character);
            }
        }

        foreach (ServerParty party in m_parties.Parties)
        {
            SendStatuses(party, context.Tick);
        }
    }

    private void SendRoster(ClientSession session, CharacterSession character)
    {
        if (!m_parties.TryGetParty(character.Character, out ServerParty? party))
        {
            m_sender.Send(session.Connection, NoParty);
            return;
        }

        party!.LastRoster ??= RosterOf(party);
        m_sender.Send(session.Connection, party.LastRoster);
        foreach (StoredPartyMember member in party.Members)
        {
            if (member.CharacterId != character.Character.Value
                && m_sessions.TryGetCharacter(new CharacterId(member.CharacterId), out CharacterSession? other))
            {
                m_sender.Send(session.Connection, StatusOf(other!.Player));
            }
        }
    }

    // Each member's change reaches the others at most once a second; a change within that second waits for its end.
    private void SendStatuses(ServerParty party, uint tick)
    {
        foreach (StoredPartyMember member in party.Members)
        {
            if (!m_sessions.TryGetCharacter(new CharacterId(member.CharacterId), out CharacterSession? character)
                || unchecked((int)(tick - character!.PartyStatusNotBefore)) < 0)
            {
                continue;
            }

            PartyMemberStatus status = StatusOf(character.Player);
            (ushort Health, ushort Spirit) now = (status.HealthPermille, status.SpiritPermille);
            if (character.PartyStatusSent == now)
            {
                continue;
            }

            character.PartyStatusSent = now;
            character.PartyStatusNotBefore = tick + m_statusIntervalTicks;
            foreach (StoredPartyMember other in party.Members)
            {
                if (other.CharacterId != member.CharacterId
                    && TryGetListener(other.CharacterId, out ClientSession? listener))
                {
                    m_sender.Send(listener!.Connection, status);
                }
            }
        }
    }

    // A member hears its party while it is in the world with a connection; one in its reconnect grace hears the
    // whole party in the baseline of its return.
    private bool TryGetListener(long characterId, out ClientSession? listener)
    {
        listener = m_sessions.TryGetCharacter(new CharacterId(characterId), out CharacterSession? character)
            && character!.Connection?.State == SessionState.InWorld
                ? character.Connection
                : null;
        return listener != null;
    }

    // A member in its reconnect grace is listed as in the world (Gameplay Systems §14); one away shows what it last
    // was.
    private PartyRoster RosterOf(ServerParty party)
    {
        m_entries.Clear();
        byte leaderIndex = 0;
        foreach (StoredPartyMember member in party.Members)
        {
            if (member.CharacterId == party.LeaderCharacterId)
            {
                leaderIndex = (byte)m_entries.Count;
            }

            m_entries.Add(
                m_sessions.TryGetCharacter(new CharacterId(member.CharacterId), out CharacterSession? character)
                    ? new PartyRosterEntry(
                        member.Name,
                        character!.Player.Job,
                        LevelOf(character.Player.Level),
                        character.Map.Definition.Id)
                    : new PartyRosterEntry(
                        member.Name,
                        new JobDefinitionId(member.JobDefinitionId),
                        LevelOf(member.BaseLevel),
                        null));
        }

        return new PartyRoster(leaderIndex, m_entries.ToArray());
    }

    private static ushort LevelOf(int level)
    {
        return (ushort)Math.Clamp(level, 1, ushort.MaxValue);
    }

    private static bool IsSame(PartyRoster roster, PartyRoster? last)
    {
        if (last == null || last.LeaderIndex != roster.LeaderIndex || last.Members.Count != roster.Members.Count)
        {
            return false;
        }

        for (int index = 0; index < roster.Members.Count; index++)
        {
            PartyRosterEntry entry = roster.Members[index];
            PartyRosterEntry previous = last.Members[index];
            if (entry.Name != previous.Name
                || entry.Job != previous.Job
                || entry.BaseLevel != previous.BaseLevel
                || entry.Map != previous.Map)
            {
                return false;
            }
        }

        return true;
    }

    // Thousandths of the maximums; a living member never shows 0 health, and a dead one always does.
    private static PartyMemberStatus StatusOf(PlayerEntity player)
    {
        ushort health = player.IsDead ? (ushort)0 : Permille(player.CurrentHealth, player.MaxHealth, 1);
        ushort spirit = Permille(player.CurrentSpirit, player.MaxSpirit, 0);
        return new PartyMemberStatus(player.Name, health, spirit);
    }

    private static ushort Permille(int current, int maximum, int least)
    {
        if (maximum <= 0 || current <= 0)
        {
            return 0;
        }

        return (ushort)Math.Clamp((long)current * 1000 / maximum, least, 1000);
    }
}
}
