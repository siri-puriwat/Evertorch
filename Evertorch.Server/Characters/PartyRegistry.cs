using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The parties of the characters in the world, and the invites between them (Gameplay Systems §14; Persistence §5,
///     §7). A party is loaded with the first of its members to enter, changes only through a commit that is answered
///     once it is back, and is dropped once none of its members is in the world and no change of it is in flight.
///     Invites live here, in memory only. Tick thread only.
/// </summary>
public sealed class PartyRegistry : ITickPhase
{
    public const int MaxMembers = 5;
    public const int InviteLifetimeMs = 30000;
    private const int MillisecondsPerSecond = 1000;
    private const string LookupOperation = "party lookup";

    // A lookup that failed for a reason other than an outage is asked again only after this long (Persistence §9), as
    // the item actions' are.
    private const int FailedJobDelayMs = 1000;

    private static readonly Action<ILogger, long, long, long, Exception?> LogCreated =
        LoggerMessage.Define<long, long, long>(
            LogLevel.Information,
            new EventId(1017, "PartyCreated"),
            "Character {Leader} founded party {Party} with character {Member}.");

    private static readonly Action<ILogger, long, long, Exception?> LogJoined =
        LoggerMessage.Define<long, long>(
            LogLevel.Information,
            new EventId(1018, "PartyJoined"),
            "Character {Character} joined party {Party}.");

    private static readonly Action<ILogger, long, long, Exception?> LogLeft =
        LoggerMessage.Define<long, long>(
            LogLevel.Information,
            new EventId(1019, "PartyLeft"),
            "Character {Character} left party {Party}.");

    private static readonly Action<ILogger, long, long, long, Exception?> LogKicked =
        LoggerMessage.Define<long, long, long>(
            LogLevel.Information,
            new EventId(1020, "PartyKicked"),
            "Character {Leader} removed character {Character} from party {Party}.");

    private static readonly Action<ILogger, long, long, Exception?> LogLeaderChanged =
        LoggerMessage.Define<long, long>(
            LogLevel.Information,
            new EventId(1021, "PartyLeaderChanged"),
            "Character {Character} leads party {Party}.");

    private static readonly Action<ILogger, long, Exception?> LogDisbanded =
        LoggerMessage.Define<long>(
            LogLevel.Information,
            new EventId(1022, "PartyDisbanded"),
            "Party {Party} disbanded.");

    private static readonly Action<ILogger, PartyChangeKind, long, long, Exception?> LogUnsettled =
        LoggerMessage.Define<PartyChangeKind, long, long>(
            LogLevel.Warning,
            new EventId(4011, "PartyChangeUnsettled"),
            "The party {Kind} asked by character {Character} of party {Party} gave no answer; the party changes no "
            + "further until the database says what happened.");

    private readonly SessionRegistry m_sessions;
    private readonly PersistenceWorker m_persistence;
    private readonly MessageSender m_sender;
    private readonly TimeProvider m_time;
    private readonly ServerInstruments m_instruments;
    private readonly AuditLog m_audit;
    private readonly ILogger<PartyRegistry> m_logger;
    private readonly uint m_inviteTicks;
    private readonly uint m_failedJobDelayTicks;
    private readonly Dictionary<long, ServerParty> m_parties = new();

    // Every member of every party held, online or not.
    private readonly Dictionary<long, ServerParty> m_partyOf = new();

    // Characters without a party in a creation being committed: one at a time each.
    private readonly HashSet<long> m_creating = new();
    private readonly Dictionary<long, PendingInvite> m_invites = new();
    private readonly List<(PartyCommit Commit, uint NotBefore)> m_unsettled = new();
    private readonly List<PendingInvite> m_ended = new();
    private uint m_tick;

    public PartyRegistry(
        SessionRegistry sessions,
        CharacterLifetime lifetime,
        PersistenceWorker persistence,
        MessageSender sender,
        TimeProvider time,
        IOptions<SimulationOptions> simulation,
        ServerInstruments instruments,
        AuditLog audit,
        ILogger<PartyRegistry> logger)
    {
        m_sessions = sessions;
        m_persistence = persistence;
        m_sender = sender;
        m_time = time;
        m_instruments = instruments;
        m_audit = audit;
        m_logger = logger;
        int tickRate = simulation.Value.TickRate;
        m_inviteTicks = (uint)((long)InviteLifetimeMs * tickRate / MillisecondsPerSecond);
        m_failedJobDelayTicks = (uint)((long)FailedJobDelayMs * tickRate / MillisecondsPerSecond);
        lifetime.Left += OnLeft;
    }

    public int PartyCount => m_parties.Count;

    public int PendingInvites => m_invites.Count;

    public IReadOnlyCollection<ServerParty> Parties => m_parties.Values;


    public TickPhase Phase => TickPhase.SchedulePersistence;

    /// <summary>
    ///     Queues the lost-answer lookups that could not be queued before, and ends the invites whose time is up,
    ///     telling their inviters.
    /// </summary>
    public void Execute(in TickContext context)
    {
        m_tick = context.Tick;
        for (int index = m_unsettled.Count - 1; index >= 0; index--)
        {
            (PartyCommit commit, uint notBefore) = m_unsettled[index];
            if (IsDue(notBefore) && TryQueueLookup(commit))
            {
                m_unsettled.RemoveAt(index);
            }
        }

        ExpireInvites();
    }

    /// <summary>
    ///     The party of <paramref name="character" />, while the server holds it.
    /// </summary>
    public bool TryGetParty(CharacterId character, out ServerParty? party)
    {
        return m_partyOf.TryGetValue(character.Value, out party);
    }

    /// <summary>
    ///     Invites the reachable character <paramref name="name" /> to the sender's party, or to a new one (Gameplay
    ///     Systems §14). Nothing is committed until the invitee accepts. The caller has refused a character logging out.
    /// </summary>
    public CommandRejectionReason TryInvite(ClientSession session, string name)
    {
        CharacterSession inviter = session.Character!;
        if (!m_sessions.TryGetReachable(name, out CharacterSession? invitee))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        ServerParty? party = PartyOf(inviter);
        if (ReferenceEquals(invitee, inviter)
            || (party != null && party.LeaderCharacterId != IdOf(inviter))
            || PartyOf(invitee!) != null
            || m_invites.ContainsKey(IdOf(invitee!)))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (party != null
                ? party.Members.Count >= MaxMembers || party.HasAccount(invitee!.Account)
                : invitee!.Account == inviter.Account)
        {
            return CommandRejectionReason.RequirementNotMet;
        }

        m_invites.Add(IdOf(invitee), new PendingInvite(inviter, invitee, party?.Id ?? 0, m_tick + m_inviteTicks));
        Tell(invitee, new PartyEvent(PartyEventKind.Invited, inviter.Player.Name));
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Answers the invite from <paramref name="inviterName" />. A decline tells the inviter at once; an accept checks
    ///     everything again against the party the invite named, or none, and commits the creation or the join.
    /// </summary>
    public CommandRejectionReason TryReply(
        ClientSession session,
        string inviterName,
        bool isAccepted,
        uint commandSequence)
    {
        CharacterSession invitee = session.Character!;
        long id = IdOf(invitee);
        if (!m_invites.TryGetValue(id, out PendingInvite? invite)
            || !string.Equals(invite.Inviter.Player.Name, inviterName, StringComparison.OrdinalIgnoreCase))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        CharacterSession inviter = invite.Inviter;
        if (!isAccepted)
        {
            m_invites.Remove(id);
            Tell(inviter, new PartyEvent(PartyEventKind.Declined, invitee.Player.Name));
            return CommandRejectionReason.None;
        }

        ServerParty? party = PartyOf(inviter);
        if (!IsOpen(invite, party))
        {
            m_invites.Remove(id);
            return CommandRejectionReason.InvalidTarget;
        }

        if (PartyOf(invitee) != null)
        {
            m_invites.Remove(id);
            return CommandRejectionReason.NotAllowedNow;
        }

        if ((party?.IsChanging ?? m_creating.Contains(IdOf(inviter))) || m_creating.Contains(id))
        {
            return CommandRejectionReason.Busy;
        }

        if (party != null && (party.Members.Count >= MaxMembers || party.HasAccount(invitee.Account)))
        {
            m_invites.Remove(id);
            return CommandRejectionReason.RequirementNotMet;
        }

        DateTime at = m_time.GetUtcNow().UtcDateTime;
        PartyChange change = party == null
            ? PartyChange.Create(IdOf(inviter), id, at)
            : PartyChange.Join(party.Id, IdOf(inviter), id, MaxMembers, at);
        CommandRejectionReason refusal = TryCommit(
            new PartyCommit(change, InboundEventKind.PartyReply, invitee, commandSequence, party, invitee.Player.Name,
                0));
        if (refusal == CommandRejectionReason.None)
        {
            m_invites.Remove(id);
        }

        return refusal;
    }

    /// <summary>
    ///     Commits the sender's departure: a leader's passes the lead to the earliest joined, and a party left with one
    ///     member disbands.
    /// </summary>
    public CommandRejectionReason TryLeave(ClientSession session, uint commandSequence)
    {
        CharacterSession member = session.Character!;
        ServerParty? party = PartyOf(member);
        if (party == null)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (party.IsChanging)
        {
            return CommandRejectionReason.Busy;
        }

        return TryCommit(
            new PartyCommit(
                PartyChange.Leave(party.Id, IdOf(member)),
                InboundEventKind.PartyLeave,
                member,
                commandSequence,
                party,
                member.Player.Name,
                WitnessOf(party, IdOf(member))));
    }

    /// <summary>
    ///     The leader removes the member named <paramref name="name" />, online or not.
    /// </summary>
    public CommandRejectionReason TryKick(ClientSession session, string name, uint commandSequence)
    {
        CommandRejectionReason refusal = CheckLeaderNaming(session, name, out ServerParty? party,
            out StoredPartyMember? member);
        if (refusal != CommandRejectionReason.None)
        {
            return refusal;
        }

        CharacterSession leader = session.Character!;
        return TryCommit(
            new PartyCommit(
                PartyChange.Kick(party!.Id, IdOf(leader), member!.CharacterId),
                InboundEventKind.PartyKick,
                leader,
                commandSequence,
                party,
                member.Name,
                WitnessOf(party, member.CharacterId)));
    }

    /// <summary>
    ///     The leader passes the lead to the member named <paramref name="name" />, online or not.
    /// </summary>
    public CommandRejectionReason TryLead(ClientSession session, string name, uint commandSequence)
    {
        CommandRejectionReason refusal = CheckLeaderNaming(session, name, out ServerParty? party,
            out StoredPartyMember? member);
        if (refusal != CommandRejectionReason.None)
        {
            return refusal;
        }

        CharacterSession leader = session.Character!;
        return TryCommit(
            new PartyCommit(
                PartyChange.Lead(party!.Id, IdOf(leader), member!.CharacterId),
                InboundEventKind.PartyLead,
                leader,
                commandSequence,
                party,
                member.Name,
                0));
    }

    private CommandRejectionReason CheckLeaderNaming(
        ClientSession session,
        string name,
        out ServerParty? party,
        out StoredPartyMember? member)
    {
        CharacterSession leader = session.Character!;
        party = PartyOf(leader);
        member = null;
        if (party == null || party.LeaderCharacterId != IdOf(leader))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (!party.TryGetMember(name, out member))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        if (member!.CharacterId == IdOf(leader))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        return party.IsChanging ? CommandRejectionReason.Busy : CommandRejectionReason.None;
    }

    private CommandRejectionReason TryCommit(PartyCommit commit)
    {
        PartyChange change = commit.Change;
        var job = new PersistenceJob<PartyChangeResult>(
            OperationOf(change.Kind),
            ConnectionOf(commit.Requester),
            IdOf(commit.Requester),
            (store, cancellation) => store.CommitPartyChangeAsync(change, cancellation),
            (outcome, result) => CompleteCommit(commit, outcome, result));
        if (!m_persistence.TryEnqueue(job))
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        if (commit.Party != null)
        {
            commit.Party.IsChanging = true;
        }
        else
        {
            m_creating.Add(change.Actor);
            m_creating.Add(change.Target);
        }

        return CommandRejectionReason.None;
    }

    private static string OperationOf(PartyChangeKind kind)
    {
        return kind switch
        {
            PartyChangeKind.Create => "party create",
            PartyChangeKind.Join => "party join",
            PartyChangeKind.Leave => "party leave",
            PartyChangeKind.Kick => "party kick",
            _ => "party lead"
        };
    }

    private void CompleteCommit(PartyCommit commit, PersistenceOutcome outcome, PartyChangeResult result)
    {
        if (outcome == PersistenceOutcome.Succeeded)
        {
            if (result.Status == PartyChangeStatus.Committed)
            {
                Apply(commit, result.Party);
            }
            else
            {
                Refuse(commit, ReasonFor(commit.Change.Kind, result.Status));
            }

            return;
        }

        // The change may have happened; only the stored membership can say.
        LogUnsettled(m_logger, commit.Change.Kind, IdOf(commit.Requester), commit.Change.PartyId, null);
        if (!TryQueueLookup(commit))
        {
            m_unsettled.Add((commit, m_tick));
        }
    }

    // The database refused what the server's copy allowed only after a race the copy cannot see; each refusal keeps
    // the meaning the command's own checks give it.
    private static CommandRejectionReason ReasonFor(PartyChangeKind kind, PartyChangeStatus status)
    {
        return status switch
        {
            PartyChangeStatus.PartyFull => CommandRejectionReason.RequirementNotMet,
            PartyChangeStatus.SameAccount => CommandRejectionReason.RequirementNotMet,
            PartyChangeStatus.NotAMember when kind == PartyChangeKind.Kick || kind == PartyChangeKind.Lead =>
                CommandRejectionReason.InvalidTarget,
            PartyChangeStatus.NoSuchParty when kind == PartyChangeKind.Join => CommandRejectionReason.InvalidTarget,
            _ => CommandRejectionReason.NotAllowedNow
        };
    }

    private bool TryQueueLookup(PartyCommit commit)
    {
        long target = commit.Change.Target;
        long witness = commit.Witness;
        var lookup = new PersistenceJob<(StoredParty? OfTarget, StoredParty? OfWitness)>(
            LookupOperation,
            ConnectionOf(commit.Requester),
            IdOf(commit.Requester),
            (store, cancellation) => FindAsync(store, target, witness, cancellation),
            (outcome, found) => CompleteLookup(commit, outcome, found.OfTarget, found.OfWitness));
        return m_persistence.TryEnqueue(lookup);
    }

    private static async Task<(StoredParty? OfTarget, StoredParty? OfWitness)> FindAsync(
        IGameStore store,
        long target,
        long witness,
        CancellationToken cancellation)
    {
        StoredParty? ofTarget = await store.FindPartyStateAsync(target, cancellation).ConfigureAwait(false);
        StoredParty? ofWitness = witness == 0
            ? null
            : await store.FindPartyStateAsync(witness, cancellation).ConfigureAwait(false);
        return (ofTarget, ofWitness);
    }

    private void CompleteLookup(
        PartyCommit commit,
        PersistenceOutcome outcome,
        StoredParty? ofTarget,
        StoredParty? ofWitness)
    {
        if (outcome != PersistenceOutcome.Succeeded)
        {
            uint notBefore = outcome == PersistenceOutcome.Failed ? m_tick + m_failedJobDelayTicks : m_tick;
            m_unsettled.Add((commit, notBefore));
            return;
        }

        if (HasCommitted(commit.Change, ofTarget, ofWitness, out StoredParty? after))
        {
            Apply(commit, after);
        }
        else
        {
            Refuse(commit, CommandRejectionReason.ServiceUnavailable);
        }
    }

    // The stored membership of the character the change named, and for a departure that of a member who stays, say
    // whether the change took effect and what the party became (Persistence §5).
    private static bool HasCommitted(
        PartyChange change,
        StoredParty? ofTarget,
        StoredParty? ofWitness,
        out StoredParty? after)
    {
        switch (change.Kind)
        {
            case PartyChangeKind.Create:
                after = ofTarget;
                return ofTarget != null && Contains(ofTarget, change.Actor);
            case PartyChangeKind.Join:
                after = ofTarget;
                return ofTarget?.Id == change.PartyId;
            case PartyChangeKind.Lead:
                after = ofTarget;
                return ofTarget?.Id == change.PartyId && ofTarget.LeaderCharacterId == change.Target;
            default:
                after = ofWitness?.Id == change.PartyId ? ofWitness : null;
                return ofTarget?.Id != change.PartyId;
        }
    }

    private void Apply(PartyCommit commit, StoredParty? after)
    {
        EndFlight(commit);
        PartyChange change = commit.Change;
        ServerParty? party = commit.Party;
        switch (change.Kind)
        {
            case PartyChangeKind.Create:
                party = new ServerParty(after!);
                m_parties.Add(party.Id, party);
                Map(party);
                LogCreated(m_logger, change.Actor, party.Id, change.Target, null);
                TellMembers(party.Members, new PartyEvent(PartyEventKind.Joined, commit.Name));
                break;
            case PartyChangeKind.Join:
                Replace(party!, after!);
                LogJoined(m_logger, change.Target, party!.Id, null);
                TellMembers(party.Members, new PartyEvent(PartyEventKind.Joined, commit.Name));
                break;
            case PartyChangeKind.Lead:
                Replace(party!, after!);
                LogLeaderChanged(m_logger, change.Target, party!.Id, null);
                TellMembers(party.Members, new PartyEvent(PartyEventKind.LeaderChanged, commit.Name));
                break;
            default:
                Depart(commit, party!, after);
                break;
        }

        m_instruments.RecordPartyChange(change.Kind);
        ReviewInvites();
        DropIfIdle(party!);
    }

    // Everyone who was a member hears of the departure, the one who went included; those who stay hear of a new
    // leader.
    private void Depart(PartyCommit commit, ServerParty party, StoredParty? after)
    {
        PartyChange change = commit.Change;
        IReadOnlyList<StoredPartyMember> before = party.Members;
        bool isKick = change.Kind == PartyChangeKind.Kick;
        if (isKick)
        {
            LogKicked(m_logger, change.Actor, change.Target, party.Id, null);
        }
        else
        {
            LogLeft(m_logger, change.Target, party.Id, null);
        }

        if (after == null)
        {
            Remove(party);
            LogDisbanded(m_logger, party.Id, null);
            TellMembers(before, new PartyEvent(PartyEventKind.Disbanded, commit.Name));
            foreach (StoredPartyMember member in before)
            {
                OweRoster(member.CharacterId);
            }

            return;
        }

        long leader = party.LeaderCharacterId;
        Replace(party, after);
        OweRoster(change.Target);
        TellMembers(before, new PartyEvent(isKick ? PartyEventKind.Kicked : PartyEventKind.Left, commit.Name));
        if (after.LeaderCharacterId != leader)
        {
            LogLeaderChanged(m_logger, after.LeaderCharacterId, party.Id, null);
            TellMembers(after.Members,
                new PartyEvent(PartyEventKind.LeaderChanged, NameOf(after, after.LeaderCharacterId)));
        }
    }

    // One who is no longer a member hears the roster of no party; members hear theirs when it changes.
    private void OweRoster(long characterId)
    {
        if (m_sessions.TryGetCharacter(new CharacterId(characterId), out CharacterSession? character)
            && character!.Connection != null)
        {
            character.Connection.NeedsPartyRoster = true;
        }
    }

    private void Refuse(PartyCommit commit, CommandRejectionReason reason)
    {
        EndFlight(commit);
        CharacterSession requester = commit.Requester;
        ClientSession? owner = requester.Connection;
        if (owner != null && owner.State == SessionState.InWorld && IsInWorld(requester))
        {
            owner.RefusedCommands++;
            m_audit.CommandRefused(owner, commit.Command, reason);
            m_sender.Send(owner.Connection, new CommandRejected(commit.CommandSequence, reason));
        }

        if (commit.Party != null)
        {
            DropIfIdle(commit.Party);
        }
    }

    private void EndFlight(PartyCommit commit)
    {
        if (commit.Party != null)
        {
            commit.Party.IsChanging = false;
            return;
        }

        m_creating.Remove(commit.Change.Actor);
        m_creating.Remove(commit.Change.Target);
    }

    // An invite stays open while its inviter still leads the party it named, or still has none, and its invitee has
    // none. A party the inviter founds through another invite carries its other invites along, since it leads it.
    private void ReviewInvites()
    {
        m_ended.Clear();
        foreach (PendingInvite invite in m_invites.Values)
        {
            ServerParty? party = PartyOf(invite.Inviter);
            if (invite.PartyId == 0 && party != null && party.LeaderCharacterId == IdOf(invite.Inviter))
            {
                invite.PartyId = party.Id;
            }

            if (!IsOpen(invite, party) || PartyOf(invite.Invitee) != null)
            {
                m_ended.Add(invite);
            }
        }

        foreach (PendingInvite invite in m_ended)
        {
            m_invites.Remove(IdOf(invite.Invitee));
        }
    }

    private bool IsOpen(PendingInvite invite, ServerParty? partyOfInviter)
    {
        return IsInWorld(invite.Inviter)
            && (partyOfInviter?.Id ?? 0) == invite.PartyId
            && (partyOfInviter == null || partyOfInviter.LeaderCharacterId == IdOf(invite.Inviter));
    }

    private void ExpireInvites()
    {
        m_ended.Clear();
        foreach (PendingInvite invite in m_invites.Values)
        {
            if (IsDue(invite.ExpiresTick))
            {
                m_ended.Add(invite);
            }
        }

        foreach (PendingInvite invite in m_ended)
        {
            m_invites.Remove(IdOf(invite.Invitee));
            Tell(invite.Inviter, new PartyEvent(PartyEventKind.Expired, invite.Invitee.Player.Name));
        }
    }

    /// <summary>
    ///     A character came into the world with <paramref name="stored" />, the party its load read, if any (Persistence
    ///     §7). Every change of a party goes through the copy held here once it is held, so that copy is never older than
    ///     a load; a party nobody holds has no change in flight, so its load is current.
    /// </summary>
    public void Enter(CharacterSession character, StoredParty? stored)
    {
        if (!m_partyOf.ContainsKey(IdOf(character)) && stored != null && !m_parties.ContainsKey(stored.Id))
        {
            var party = new ServerParty(stored);
            m_parties.Add(party.Id, party);
            Map(party);
        }
    }

    // Its invites end with it; its party is dropped once no member is left in the world.
    private void OnLeft(CharacterSession character)
    {
        long id = IdOf(character);
        m_invites.Remove(id);
        m_ended.Clear();
        foreach (PendingInvite invite in m_invites.Values)
        {
            if (IdOf(invite.Inviter) == id)
            {
                m_ended.Add(invite);
            }
        }

        foreach (PendingInvite invite in m_ended)
        {
            m_invites.Remove(IdOf(invite.Invitee));
        }

        if (m_partyOf.TryGetValue(id, out ServerParty? party))
        {
            PlayerEntity player = character.Player;
            party.Remember(id, player.Job.Value, player.Level);
            DropIfIdle(party);
        }
    }

    private void DropIfIdle(ServerParty party)
    {
        if (party.IsChanging || !m_parties.ContainsKey(party.Id))
        {
            return;
        }

        foreach (StoredPartyMember member in party.Members)
        {
            if (m_sessions.TryGetCharacter(new CharacterId(member.CharacterId), out _))
            {
                return;
            }
        }

        Remove(party);
    }

    private void Map(ServerParty party)
    {
        foreach (StoredPartyMember member in party.Members)
        {
            m_partyOf[member.CharacterId] = party;
        }
    }

    private void Replace(ServerParty party, StoredParty stored)
    {
        foreach (StoredPartyMember member in party.Members)
        {
            if (!Contains(stored, member.CharacterId))
            {
                m_partyOf.Remove(member.CharacterId);
            }
        }

        party.Replace(stored);
        Map(party);
    }

    private void Remove(ServerParty party)
    {
        m_parties.Remove(party.Id);
        foreach (StoredPartyMember member in party.Members)
        {
            if (m_partyOf.TryGetValue(member.CharacterId, out ServerParty? mapped) && ReferenceEquals(mapped, party))
            {
                m_partyOf.Remove(member.CharacterId);
            }
        }
    }

    private void TellMembers(IReadOnlyList<StoredPartyMember> members, PartyEvent message)
    {
        foreach (StoredPartyMember member in members)
        {
            if (m_sessions.TryGetCharacter(new CharacterId(member.CharacterId), out CharacterSession? character))
            {
                Tell(character!, message);
            }
        }
    }

    // Only a character in the world with a connection hears anything; one in its reconnect grace learns from the
    // roster when it comes back.
    private void Tell(CharacterSession character, PartyEvent message)
    {
        ClientSession? owner = character.Connection;
        if (owner != null && owner.State == SessionState.InWorld && IsInWorld(character))
        {
            m_sender.Send(owner.Connection, message);
        }
    }

    private ServerParty? PartyOf(CharacterSession character)
    {
        return m_partyOf.TryGetValue(IdOf(character), out ServerParty? party) ? party : null;
    }

    private bool IsInWorld(CharacterSession character)
    {
        return m_sessions.TryGetCharacter(character.Character, out CharacterSession? registered)
            && ReferenceEquals(registered, character);
    }

    private bool IsDue(uint notBefore)
    {
        return unchecked((int)(m_tick - notBefore)) >= 0;
    }

    // A member who stays, whose stored party tells what a departure left.
    private static long WitnessOf(ServerParty party, long departing)
    {
        foreach (StoredPartyMember member in party.Members)
        {
            if (member.CharacterId != departing)
            {
                return member.CharacterId;
            }
        }

        return 0;
    }

    private static bool Contains(StoredParty party, long characterId)
    {
        foreach (StoredPartyMember member in party.Members)
        {
            if (member.CharacterId == characterId)
            {
                return true;
            }
        }

        return false;
    }

    private static string NameOf(StoredParty party, long characterId)
    {
        foreach (StoredPartyMember member in party.Members)
        {
            if (member.CharacterId == characterId)
            {
                return member.Name;
            }
        }

        return string.Empty;
    }

    private static long IdOf(CharacterSession character)
    {
        return character.Character.Value;
    }

    private static ConnectionId ConnectionOf(CharacterSession character)
    {
        return character.Connection?.Connection ?? default;
    }

    private sealed class PendingInvite
    {
        public PendingInvite(CharacterSession inviter, CharacterSession invitee, long partyId, uint expiresTick)
        {
            Inviter = inviter;
            Invitee = invitee;
            PartyId = partyId;
            ExpiresTick = expiresTick;
        }

        public CharacterSession Inviter { get; }

        public CharacterSession Invitee { get; }

        /// <summary>
        ///     The party the invitee would join; 0 for a new one.
        /// </summary>
        public long PartyId { get; set; }

        public uint ExpiresTick { get; }
    }

    private sealed class PartyCommit
    {
        public PartyCommit(
            PartyChange change,
            InboundEventKind command,
            CharacterSession requester,
            uint commandSequence,
            ServerParty? party,
            string name,
            long witness)
        {
            Change = change;
            Command = command;
            Requester = requester;
            CommandSequence = commandSequence;
            Party = party;
            Name = name;
            Witness = witness;
        }

        public PartyChange Change { get; }

        public InboundEventKind Command { get; }

        public CharacterSession Requester { get; }

        public uint CommandSequence { get; }

        /// <summary>
        ///     The party changed; null for a creation.
        /// </summary>
        public ServerParty? Party { get; }

        /// <summary>
        ///     The name the members hear: the one who joined, left, or was removed, or the new leader.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     For a departure, a member who stays.
        /// </summary>
        public long Witness { get; }
    }
}
}
