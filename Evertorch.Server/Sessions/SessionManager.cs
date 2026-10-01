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
///     First phase of every tick: turns what arrived from the network since the last tick into session and world
///     changes. It is the only code that creates, advances, or closes a session.
/// </summary>
public sealed class SessionManager : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private static readonly Action<ILogger, long, long, DisconnectReason, Exception?> LogHandshakeRejected =
        LoggerMessage.Define<long, long, DisconnectReason>(
            LogLevel.Information,
            new EventId(2001, "HandshakeRejected"),
            "Connection {Connection} (account {Account}) was refused: {Reason}.");

    private static readonly Action<ILogger, long, long, long, long, Exception?> LogWorldEntered =
        LoggerMessage.Define<long, long, long, long>(
            LogLevel.Information,
            new EventId(2002, "WorldEntered"),
            "Connection {Connection} (account {Account}) entered the world as character {Character}, entity {Entity}.");

    private static readonly Action<ILogger, long, long, long, Exception?> LogSessionClosed =
        LoggerMessage.Define<long, long, long>(
            LogLevel.Information,
            new EventId(2003, "SessionClosed"),
            "Connection {Connection} (account {Account}, character {Character}) closed.");

    private static readonly Action<ILogger, long, long, Exception?> LogAuthenticated =
        LoggerMessage.Define<long, long>(
            LogLevel.Information,
            new EventId(2005, "SessionAuthenticated"),
            "Connection {Connection} signed in to account {Account}.");

    private static readonly Action<ILogger, long, long, long, string, Exception?> LogCharacterContentMismatch =
        LoggerMessage.Define<long, long, long, string>(
            LogLevel.Warning,
            new EventId(2006, "CharacterContentMismatch"),
            "Connection {Connection} (account {Account}) could not enter character {Character}: the loaded content has "
            + "no {Definition}. Its stored data is kept.");

    private static readonly Action<ILogger, long, long, string, string, Exception?> LogMapTransferred =
        LoggerMessage.Define<long, long, string, string>(
            LogLevel.Information,
            new EventId(2007, "MapTransferred"),
            "Connection {Connection} took character {Character} from {From} to {To}.");

    private static readonly Action<ILogger, long, string, string, Exception?> LogDeadEndPortal =
        LoggerMessage.Define<long, string, string>(
            LogLevel.Warning,
            new EventId(2008, "MapTransferSkipped"),
            "Character {Character} stands in a portal on {From} to {To}, which this server has not loaded; it stays.");

    private static readonly Action<ILogger, long, long, long, Exception?> LogSessionFaulted =
        LoggerMessage.Define<long, long, long>(
            LogLevel.Error,
            new EventId(2004, "SessionFaulted"),
            "Handling input from connection {Connection} (account {Account}, character {Character}) failed; the "
            + "connection was closed.");

    private readonly InboundQueue m_inbound;
    private readonly PersistenceWorker m_persistence;
    private readonly SessionRegistry m_sessions;
    private readonly HandshakeValidator m_handshake;
    private readonly ISessionTokenValidator m_tokens;
    private readonly WorldSimulation m_world;
    private readonly MessageSender m_sender;
    private readonly Targeting m_targeting;
    private readonly PlayerLife m_life;
    private readonly CharacterLifetime m_lifetime;
    private readonly CharacterProgression m_progression;
    private readonly CharacterBuilds m_builds;
    private readonly CombatSystem m_combat;
    private readonly PickupSystem m_pickups;
    private readonly ItemActionSystem m_items;
    private readonly ChatSystem m_chat;
    private readonly PartyRegistry m_parties;
    private readonly TimeProvider m_time;
    private readonly ServerInstruments m_instruments;
    private readonly AuditLog m_audit;
    private readonly AbuseOptions m_abuse;
    private readonly AccountCooldowns m_cooldowns;
    private readonly ILogger<SessionManager> m_logger;
    private readonly string m_serverBuildVersion;
    private readonly uint m_tickRate;
    private readonly uint m_handshakeTimeoutTicks;
    private readonly int m_maxQueuedInputs;
    private readonly float m_npcReach;
    private readonly List<ClientSession> m_expired = new();
    private readonly List<CharacterSession> m_cancelledLogouts = new();
    private readonly List<ClientSession> m_transfers = new();
    private uint m_currentTick;

    public SessionManager(
        InboundQueue inbound,
        PersistenceWorker persistence,
        SessionRegistry sessions,
        HandshakeValidator handshake,
        ISessionTokenValidator tokens,
        WorldSimulation world,
        MessageSender sender,
        Targeting targeting,
        PlayerLife life,
        CharacterLifetime lifetime,
        CharacterProgression progression,
        CharacterBuilds builds,
        CombatSystem combat,
        PickupSystem pickups,
        ItemActionSystem items,
        ChatSystem chat,
        PartyRegistry parties,
        TimeProvider time,
        IOptions<SimulationOptions> simulation,
        IOptions<NetworkOptions> network,
        IOptions<CompatibilityOptions> compatibility,
        IOptions<WorldOptions> worldOptions,
        IOptions<AbuseOptions> abuse,
        ServerInstruments instruments,
        AuditLog audit,
        ILogger<SessionManager> logger)
    {
        m_abuse = abuse.Value;
        m_audit = audit;
        m_maxQueuedInputs = worldOptions.Value.MaxQueuedInputs;
        m_npcReach = NpcInteraction.Range + worldOptions.Value.AttackRangeTolerance;
        m_inbound = inbound;
        m_persistence = persistence;
        m_sessions = sessions;
        m_handshake = handshake;
        m_tokens = tokens;
        m_world = world;
        m_sender = sender;
        m_targeting = targeting;
        m_life = life;
        m_lifetime = lifetime;
        m_progression = progression;
        m_builds = builds;
        m_combat = combat;
        m_pickups = pickups;
        m_pickups.Settled += OnOperationSettled;
        m_items = items;
        m_chat = chat;
        m_parties = parties;
        m_items.Settled += OnOperationSettled;
        m_time = time;
        m_instruments = instruments;
        m_logger = logger;
        m_serverBuildVersion = compatibility.Value.ServerBuildVersion;
        m_tickRate = (uint)simulation.Value.TickRate;

        long timeoutTicks = (long)network.Value.HandshakeTimeoutMs * simulation.Value.TickRate /
            MillisecondsPerSecond;
        m_handshakeTimeoutTicks = (uint)Math.Max(1L, timeoutTicks);
        m_cooldowns = new AccountCooldowns(
            (uint)((long)m_abuse.KickCooldownMs * simulation.Value.TickRate / MillisecondsPerSecond));
    }

    /// <summary>
    ///     Inputs that arrived malformed, out of order for the session's state, or for a connection already closed.
    /// </summary>
    public long IgnoredEvents { get; private set; }

    /// <summary>
    ///     Connections refused because they did not sign in: a rejected token, silence until the handshake timed out,
    ///     or entry asked for before the hello.
    /// </summary>
    public long AuthenticationFailures { get; private set; }

    /// <summary>
    ///     Commands refused or dropped by the per-connection command buckets, over every connection.
    /// </summary>
    public long ThrottledCommands { get; private set; }

    /// <summary>
    ///     Violations added to connections' scores, each dropped input counted.
    /// </summary>
    public long Violations { get; private set; }

    /// <summary>
    ///     Connections closed for violations or for rate excess, with <c>RateLimited</c> or <c>Kicked</c>.
    /// </summary>
    public long ViolationDisconnects { get; private set; }

    public TickPhase Phase => TickPhase.DrainCommands;

    public void Execute(in TickContext context)
    {
        m_currentTick = context.Tick;
        // Database results first, at a fixed point in the tick, so this tick's commands already see them
        // (Persistence §9).
        while (m_persistence.TryDequeueCompletion(out PersistenceJob job))
        {
            try
            {
                job.Complete();
            }
            catch (Exception exception)
            {
                CloseFaulted(job.Connection, exception);
            }
        }

        CancelLogoutsWhileUnavailable();
        while (m_inbound.TryDequeue(out InboundEvent inboundEvent))
        {
            try
            {
                Handle(inboundEvent, context.Tick);
            }
            catch (Exception exception)
            {
                CloseFaulted(inboundEvent.Connection, exception);
            }
        }

        ExpireSilentConnections(context.Tick);
        m_lifetime.ExpireGracePeriods(context.Tick);
    }

    private void Handle(InboundEvent inboundEvent, uint tick)
    {
        if (inboundEvent.Kind == InboundEventKind.Connected)
        {
            var connected = new ClientSession(inboundEvent.Connection, tick);
            if (m_abuse.Enabled)
            {
                connected.CommandLimits = new SessionCommandLimits(m_abuse, (int)m_tickRate, tick);
                connected.Violations = new ViolationScore(m_abuse, (int)m_tickRate, tick);
            }

            m_sessions.Add(connected);
            return;
        }

        if (!m_sessions.TryGet(inboundEvent.Connection, out ClientSession? session) || session == null)
        {
            IgnoredEvents++;
            return;
        }

        switch (inboundEvent.Kind)
        {
            case InboundEventKind.Disconnected:
                Remove(session);
                break;
            case InboundEventKind.RateLimited:
                Expel(session, DisconnectReason.RateLimited);
                break;
            case InboundEventKind.InputDropped:
                Score(session, Violation.InputRate, inboundEvent.Count);
                break;
            case InboundEventKind.Malformed:
                IgnoredEvents++;
                Score(session, Violation.Malformed, 1);
                break;
            case InboundEventKind.Hello:
                HandleHello(session, inboundEvent.Hello!);
                break;
            case InboundEventKind.EnterWorld:
                HandleEnterWorld(session, inboundEvent.EnterWorld, tick);
                break;
            case InboundEventKind.CreateCharacter:
                HandleCreateCharacter(session, inboundEvent.Name!);
                break;
            case InboundEventKind.Move:
                HandleMove(session, inboundEvent.Intent, inboundEvent.MapEpoch);
                break;
            case InboundEventKind.Target:
                HandleTarget(session, inboundEvent.Target);
                break;
            case InboundEventKind.InventoryResync:
                HandleInventoryResync(session);
                break;
            case InboundEventKind.Attack:
            case InboundEventKind.Cancel:
            case InboundEventKind.UseSkill:
            case InboundEventKind.Respawn:
            case InboundEventKind.Logout:
            case InboundEventKind.Pickup:
            case InboundEventKind.Equip:
            case InboundEventKind.Unequip:
            case InboundEventKind.UseItem:
            case InboundEventKind.Buy:
            case InboundEventKind.Sell:
            case InboundEventKind.AcceptQuest:
            case InboundEventKind.CompleteQuest:
            case InboundEventKind.AllocateStat:
            case InboundEventKind.LearnSkill:
            case InboundEventKind.ResetBuild:
            case InboundEventKind.ChangeJob:
            case InboundEventKind.Chat:
            case InboundEventKind.PartyInvite:
            case InboundEventKind.PartyReply:
            case InboundEventKind.PartyLeave:
            case InboundEventKind.PartyKick:
            case InboundEventKind.PartyLead:
                HandleCommand(session, inboundEvent, tick);
                break;
            default:
                IgnoredEvents++;
                break;
        }

        // Closed once the event is handled, so an answer it earned still goes out ahead of the notice.
        DisconnectReason verdict = session.Violations?.Verdict ?? DisconnectReason.None;
        if (verdict != DisconnectReason.None && IsOpen(session))
        {
            Expel(session, verdict);
        }
    }

    private void HandleHello(ClientSession session, ClientHello hello)
    {
        if (session.State != SessionState.AwaitingHello)
        {
            IgnoredEvents++;
            Score(session, Violation.RepeatedHello, 1);
            return;
        }

        DisconnectReason refusal = m_handshake.Validate(hello);
        if (refusal != DisconnectReason.None)
        {
            Refuse(session, refusal);
            return;
        }

        // Admission needs the database (Persistence §9): without it the connection is refused as not ready.
        string token = hello.SessionToken;
        ConnectionId connection = session.Connection;
        var authentication = new PersistenceJob<SessionTokenCheck>(
            "authenticate",
            connection,
            0,
            (store, cancellation) => m_tokens.ValidateAsync(token, store, cancellation),
            (outcome, check) => CompleteAuthentication(connection, outcome, check));
        if (!m_persistence.TryEnqueueAdmission(authentication))
        {
            Refuse(session, DisconnectReason.ServerNotReady);
            return;
        }

        session.State = SessionState.Authenticating;
    }

    private void CompleteAuthentication(
        ConnectionId connection,
        PersistenceOutcome outcome,
        SessionTokenCheck? check)
    {
        // Connection numbers are never reused, so a session found here is the one that asked.
        if (!m_sessions.TryGet(connection, out ClientSession? session)
            || session == null
            || session.State != SessionState.Authenticating)
        {
            return;
        }

        AccountId? account = check?.Account;
        DisconnectReason refusal = outcome != PersistenceOutcome.Succeeded ? DisconnectReason.ServerNotReady
            : check?.Verdict == SessionTokenVerdict.Expired ? DisconnectReason.SessionExpired
            : account == null ? DisconnectReason.AuthenticationFailed
            : DisconnectReason.None;
        if (refusal != DisconnectReason.None)
        {
            Refuse(session, refusal);
            return;
        }

        // An account disconnected for violations waits out its cooldown (Network Protocol §11).
        if (m_cooldowns.IsCoolingDown(account!.Value, m_currentTick))
        {
            m_instruments.RecordRateLimited(ServerInstruments.AccountCooldownLimit);
            m_audit.ConnectionRefused(connection, account, ServerInstruments.AccountCooldownLimit);
            Refuse(session, DisconnectReason.RateLimited);
            return;
        }

        session.Account = account;
        session.State = SessionState.Authenticated;
        LogAuthenticated(m_logger, connection.Value, account!.Value.Value, null);
        m_sender.Send(
            connection,
            new ServerHello(
                ProtocolConstants.ProtocolVersion,
                m_serverBuildVersion,
                m_handshake.RequiredClientContentVersion,
                m_tickRate,
                m_time.GetUtcNow().ToUnixTimeMilliseconds()));
        QueueCharacterList(session);
    }

    // The list after ServerHello is part of admission: without the database the connection is not ready.
    private void QueueCharacterList(ClientSession session)
    {
        AccountId account = session.Account!.Value;
        ConnectionId connection = session.Connection;
        var list = new PersistenceJob<IReadOnlyList<CharacterSummary>>(
            "list characters",
            connection,
            0,
            (store, cancellation) => store.ListCharactersAsync(account, cancellation),
            (outcome, characters) => CompleteCharacterList(connection, outcome, characters));
        if (!m_persistence.TryEnqueueAdmission(list))
        {
            Refuse(session, DisconnectReason.ServerNotReady);
        }
    }

    private void CompleteCharacterList(
        ConnectionId connection,
        PersistenceOutcome outcome,
        IReadOnlyList<CharacterSummary> characters)
    {
        if (!m_sessions.TryGet(connection, out ClientSession? session) || session == null)
        {
            return;
        }

        if (outcome != PersistenceOutcome.Succeeded)
        {
            Refuse(session, DisconnectReason.ServerNotReady);
            return;
        }

        SendCharacterList(session, characters);
    }

    private void SendCharacterList(ClientSession session, IReadOnlyList<CharacterSummary> characters)
    {
        session.Characters = characters;
        var entries = new List<CharacterListEntry>(characters.Count);
        foreach (CharacterSummary character in characters)
        {
            // A job text that is not even an ID cannot travel; the character stays in the database untouched
            // (Persistence §8) but cannot be chosen.
            if (JobDefinitionId.TryCreate(character.JobDefinitionId, out JobDefinitionId job))
            {
                ushort level = (ushort)Math.Clamp(character.BaseLevel, 1, ushort.MaxValue);
                entries.Add(new CharacterListEntry(new CharacterId(character.Id), character.Name, job, level));
            }
        }

        m_sender.Send(session.Connection, new CharacterList(entries));
    }

    private void HandleCreateCharacter(ClientSession session, string name)
    {
        if (session.State != SessionState.Authenticated || session.Characters == null || session.IsCreatingCharacter)
        {
            IgnoredEvents++;
            return;
        }

        if (IsThrottled(session, InboundEventKind.CreateCharacter))
        {
            return;
        }

        CreateCharacterOutcome refusal = !CharacterNamePolicy.IsValid(name) ? CreateCharacterOutcome.NameInvalid
            : session.Characters.Count >= CharacterList.MaxEntries ? CreateCharacterOutcome.LimitReached
            : CreateCharacterOutcome.None;
        if (refusal == CreateCharacterOutcome.None)
        {
            AccountId account = session.Account!.Value;
            ConnectionId connection = session.Connection;
            NewCharacter character = m_world.CreateCharacter(name, m_time.GetUtcNow().UtcDateTime);
            var create = new PersistenceJob<CharacterCreation>(
                "create character",
                connection,
                0,
                (store, cancellation) =>
                    store.CreateCharacterAsync(account, character, CharacterList.MaxEntries, cancellation),
                (outcome, creation) => CompleteCreateCharacter(connection, outcome, creation));
            if (m_persistence.TryEnqueue(create))
            {
                session.IsCreatingCharacter = true;
                return;
            }

            refusal = CreateCharacterOutcome.ServiceUnavailable;
        }

        m_sender.Send(session.Connection, new CreateCharacterResult(refusal, default));
        SendCharacterList(session, session.Characters);
    }

    private void CompleteCreateCharacter(ConnectionId connection, PersistenceOutcome outcome,
        CharacterCreation creation)
    {
        if (!m_sessions.TryGet(connection, out ClientSession? session) || session == null)
        {
            return;
        }

        session.IsCreatingCharacter = false;
        if (outcome != PersistenceOutcome.Succeeded)
        {
            m_sender.Send(connection, new CreateCharacterResult(CreateCharacterOutcome.ServiceUnavailable, default));
            SendCharacterList(session, session.Characters ?? Array.Empty<CharacterSummary>());
            return;
        }

        CreateCharacterOutcome result = creation.Status switch
        {
            CharacterCreationStatus.Created => CreateCharacterOutcome.Created,
            CharacterCreationStatus.NameTaken => CreateCharacterOutcome.NameTaken,
            _ => CreateCharacterOutcome.LimitReached
        };
        m_sender.Send(connection, new CreateCharacterResult(result, new CharacterId(creation.CharacterId)));
        SendCharacterList(session, creation.Characters);
    }

    private void HandleEnterWorld(ClientSession session, EnterWorldRequest request, uint tick)
    {
        if (session.State == SessionState.AwaitingHello)
        {
            Refuse(session, DisconnectReason.AuthenticationFailed);
            return;
        }

        // A character of another account is refused exactly like one that does not exist (Network Protocol §4), and
        // so is one another connection is still loading.
        if (session.State != SessionState.Authenticated
            || !Owns(session, request.Character)
            || IsLoadingElsewhere(request.Character))
        {
            IgnoredEvents++;
            return;
        }

        if (IsThrottled(session, InboundEventKind.EnterWorld))
        {
            return;
        }

        // A character still in the world, retained or controlled by an older connection, is attached to: the same
        // entity, no second copy loaded (Network Protocol §3, Persistence §7).
        if (m_sessions.TryGetCharacter(request.Character, out CharacterSession? existing) && existing != null)
        {
            if (existing.IsLoggingOut || existing.IsExpelled)
            {
                IgnoredEvents++;
                return;
            }

            ClientSession? older = existing.Connection;
            m_lifetime.Attach(existing, session);
            if (older != null)
            {
                Close(older, DisconnectReason.SessionReplaced);
            }

            EnterAs(session, existing, m_currentTick);
            return;
        }

        AccountId account = session.Account!.Value;
        ConnectionId connection = session.Connection;
        long character = request.Character.Value;
        var load = new PersistenceJob<(StoredCharacter? Character, StoredParty? Party)>(
            "load character",
            connection,
            character,
            (store, cancellation) => LoadAsync(store, account, character, cancellation),
            (outcome, stored) => CompleteLoad(connection, outcome, stored.Character, stored.Party));
        if (!m_persistence.TryEnqueueAdmission(load))
        {
            Refuse(session, DisconnectReason.ServerNotReady);
            return;
        }

        session.State = SessionState.EnteringWorld;
        session.LoadingCharacter = request.Character;
    }

    // The party comes with the character, so the baseline carries it at once (Persistence §7).
    private static async Task<(StoredCharacter? Character, StoredParty? Party)> LoadAsync(
        IGameStore store,
        AccountId account,
        long character,
        CancellationToken cancellation)
    {
        StoredCharacter? stored =
            await store.LoadCharacterAsync(account, character, cancellation).ConfigureAwait(false);
        StoredParty? party = stored == null
            ? null
            : await store.LoadPartyAsync(character, cancellation).ConfigureAwait(false);
        return (stored, party);
    }

    private bool IsLoadingElsewhere(CharacterId character)
    {
        foreach (ClientSession other in m_sessions.Sessions)
        {
            if (other.State == SessionState.EnteringWorld && other.LoadingCharacter == character)
            {
                return true;
            }
        }

        return false;
    }

    private void CompleteLoad(
        ConnectionId connection,
        PersistenceOutcome outcome,
        StoredCharacter? stored,
        StoredParty? party)
    {
        if (!m_sessions.TryGet(connection, out ClientSession? session)
            || session == null
            || session.State != SessionState.EnteringWorld)
        {
            return;
        }

        CharacterId requested = session.LoadingCharacter;
        session.LoadingCharacter = default;
        if (outcome != PersistenceOutcome.Succeeded)
        {
            // Entering needs the database (Persistence §9): without it the connection is not ready.
            Refuse(session, DisconnectReason.ServerNotReady);
            return;
        }

        session.State = SessionState.Authenticated;
        if (stored == null)
        {
            IgnoredEvents++;
            return;
        }

        uint tick = m_currentTick;
        CharacterSession? character = m_lifetime.Spawn(stored, session, tick, out string problem);
        if (character == null)
        {
            LogCharacterContentMismatch(
                m_logger,
                session.Connection.Value,
                AccountOf(session),
                requested.Value,
                problem,
                null);
            IgnoredEvents++;
            return;
        }

        session.Character = character;
        m_parties.Enter(character, party);
        EnterAs(session, character, tick);
    }

    // The movement sequence starts afresh with the connection; the command sequence belongs to the character and
    // continues (Network Protocol §3, §8).
    private void EnterAs(ClientSession session, CharacterSession character, uint tick)
    {
        session.Input = new PlayerInputState(m_maxQueuedInputs);
        session.State = SessionState.InWorld;
        SendBaseline(session, character, tick);
        LogWorldEntered(
            m_logger,
            session.Connection.Value,
            AccountOf(session),
            character.Character.Value,
            character.Player.Id.Value,
            null);
    }

    // The full baseline starts here: WorldEntered now, then a spawn for everything in view from this tick's visibility
    // pass, since the client knows no entity of this map yet, then the whole inventory, the skill list, the status
    // effects, and the quests (Network Protocol §9). Entering, an attach, and a map change all send it.
    private void SendBaseline(ClientSession session, CharacterSession character, uint tick)
    {
        session.KnownEntities.Clear();
        session.NeedsInventorySnapshot = true;
        session.NeedsSkillList = true;
        session.NeedsStatusEffects = true;
        session.NeedsQuestLog = true;
        session.NeedsPartyRoster = true;
        PlayerEntity player = character.Player;
        MapInstance map = character.Map;
        m_sender.Send(
            session.Connection,
            new WorldEntered(
                map.Definition.Id,
                map.InstanceNumber,
                player.Id,
                player.Job,
                tick,
                player.Position,
                player.Facing,
                player.MovementSpeed,
                (uint)player.CurrentHealth,
                (uint)player.MaxHealth,
                player.AttackRange,
                character.LastCommandSequence,
                character.Character,
                (ushort)player.Level,
                (ulong)player.Experience,
                (ulong)m_progression.ExperienceToNextLevel(player),
                (uint)player.CurrentSpirit,
                (uint)player.MaxSpirit,
                session.MapEpoch));

        // The owner's sheet comes right after WorldEntered in every baseline (Network Protocol §9).
        CharacterSheet sheet = m_builds.SheetOf(player);
        session.LastSheet = sheet;
        m_sender.Send(session.Connection, sheet);
    }

    /// <summary>
    ///     Moves every character that stands in a portal to its destination (Gameplay Systems §4.2). A dead,
    ///     logging-out, or expelled character stays, and one with an inventory operation in flight waits for it to
    ///     settle.
    /// </summary>
    public void TransferThroughPortals(uint tick)
    {
        m_transfers.Clear();
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Character != null)
            {
                m_transfers.Add(session);
            }
        }

        foreach (ClientSession session in m_transfers)
        {
            try
            {
                TryTransfer(session, session.Character!, tick);
            }
            catch (Exception exception)
            {
                CloseFaulted(session.Connection, exception);
            }
        }
    }

    private void TryTransfer(ClientSession session, CharacterSession character, uint tick)
    {
        PlayerEntity player = character.Player;
        MapInstance origin = character.Map;
        MapPortal? portal = null;
        foreach (MapPortal candidate in origin.Definition.Portals)
        {
            if (candidate.Contains(player.Position))
            {
                portal = candidate;
                break;
            }
        }

        if (portal == null)
        {
            character.IsInDeadEndPortal = false;
            return;
        }

        if (player.IsDead || character.IsLoggingOut || character.IsExpelled || character.Operation != null)
        {
            return;
        }

        // Each crossing sends a whole baseline and queues a checkpoint, so one that just arrived crosses again only a
        // second later, however it steps back and forth across the gate.
        if (character.ArrivedThroughPortalTick is uint arrived && unchecked(tick - arrived) < m_tickRate)
        {
            return;
        }

        WorldPosition arrival = portal.DestinationPosition;
        if (!m_world.TryGetMap(portal.DestinationMap, out MapInstance? destination)
            || destination == null
            || !destination.Definition.Navigation.CanOccupy(arrival.X, arrival.Z))
        {
            if (!character.IsInDeadEndPortal)
            {
                character.IsInDeadEndPortal = true;
                LogDeadEndPortal(
                    m_logger,
                    character.Character.Value,
                    origin.Definition.Id.Value,
                    portal.DestinationMap.Value,
                    null);
            }

            return;
        }

        // Everything aimed at the old map ends, and input queued for it is consumed and acknowledged; the epoch drops
        // what is still on its way (Network Protocol §10).
        m_combat.PrepareForTransfer(origin, player);
        session.Input!.Halt();
        m_world.RemovePlayer(origin, player);
        player.Position = arrival;
        player.Facing = MovementModel.NormalizeOrZero(portal.DestinationFacing.X, portal.DestinationFacing.Z);
        player.VelocityX = 0f;
        player.VelocityY = 0f;
        player.VelocityZ = 0f;
        player.StateFlags &= ~EntityStateFlags.Moving;
        destination.Add(player);
        character.Map = destination;
        character.ArrivedThroughPortalTick = tick;
        session.MapEpoch = unchecked((byte)(session.MapEpoch + 1));
        SendBaseline(session, character, tick);
        m_lifetime.QueueCheckpoint(character);
        m_instruments.RecordMapTransfer(destination.Definition.Id);
        LogMapTransferred(
            m_logger,
            session.Connection.Value,
            character.Character.Value,
            origin.Definition.Id.Value,
            destination.Definition.Id.Value,
            null);
    }

    private static bool Owns(ClientSession session, CharacterId character)
    {
        if (session.Characters == null)
        {
            return false;
        }

        foreach (CharacterSummary owned in session.Characters)
        {
            if (owned.Id == character.Value)
            {
                return true;
            }
        }

        return false;
    }

    // Only queued here. The movement phase applies at most one input per tick, whatever arrives.
    private void HandleMove(ClientSession session, MoveIntent intent, byte mapEpoch)
    {
        if (session.State != SessionState.InWorld || session.Input == null || session.Character!.IsLoggingOut)
        {
            IgnoredEvents++;
            return;
        }

        // Input made for the map before a transfer, still on its way; an honest client sends it, so it is not scored.
        if (mapEpoch != session.MapEpoch)
        {
            session.OtherEpochInputs++;
            m_instruments.RecordOtherEpochInput();
            return;
        }

        session.Input.Queue.TryEnqueue(intent);
    }

    // Answered once per tick however often it is asked, so a client cannot make the server send more than one
    // inventory per tick.
    private void HandleInventoryResync(ClientSession session)
    {
        if (session.State != SessionState.InWorld)
        {
            IgnoredEvents++;
            return;
        }

        if (!IsThrottled(session, InboundEventKind.InventoryResync))
        {
            session.NeedsInventorySnapshot = true;
        }
    }

    private void HandleTarget(ClientSession session, EntityId target)
    {
        if (session.State != SessionState.InWorld)
        {
            IgnoredEvents++;
            return;
        }

        if (IsThrottled(session, InboundEventKind.Target))
        {
            return;
        }

        CommandRejectionReason refusal =
            session.Player?.IsDead == true || session.Character?.IsLoggingOut == true
                ? CommandRejectionReason.NotAllowedNow
                : m_targeting.TrySelect(session, target)
                    ? CommandRejectionReason.None
                    : CommandRejectionReason.InvalidTarget;
        if (refusal != CommandRejectionReason.None)
        {
            session.RefusedCommands++;
            m_audit.CommandRefused(session, InboundEventKind.Target, refusal);
        }
    }

    // Commands carry one sequence per session (Network Protocol §8). Reliable ordered delivery never leaves a gap, so
    // a sequence that is not newer is a duplicate or a replay and is refused.
    private void HandleCommand(ClientSession session, InboundEvent command, uint tick)
    {
        if (session.State != SessionState.InWorld || session.Player == null)
        {
            IgnoredEvents++;
            return;
        }

        if (unchecked((int)(command.CommandSequence - session.LastCommandSequence)) <= 0)
        {
            session.RefusedCommands++;
            Score(session, Violation.StaleCommand, 1);
            return;
        }

        session.LastCommandSequence = command.CommandSequence;

        // Reason 3 already ends the client's chase or pickup cleanly (Network Protocol §11).
        if (IsThrottled(session, command.Kind))
        {
            Reject(session, command.CommandSequence, CommandRejectionReason.NotAllowedNow);
            return;
        }

        CommandRejectionReason refusal = Apply(session, command, tick);
        if (refusal != CommandRejectionReason.None)
        {
            m_audit.CommandRefused(session, command.Kind, refusal);
            Reject(session, command.CommandSequence, refusal);
        }
    }

    // Every refusal of a sequenced command is answered with its own sequence (Network Protocol §11); only a stale
    // sequence, which only a faulty or hostile client sends, goes unanswered.
    private CommandRejectionReason Apply(ClientSession session, InboundEvent command, uint tick)
    {
        PlayerEntity player = session.Player!;
        if (session.Character!.IsLoggingOut)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        // Chat and the party's commands are applied while dead (Network Protocol §11).
        switch (command.Kind)
        {
            case InboundEventKind.Chat:
                return m_chat.TrySend(session, command.Channel, command.Name!, command.Text!);
            case InboundEventKind.PartyInvite:
                return m_parties.TryInvite(session, command.Name!);
            case InboundEventKind.PartyReply:
                return m_parties.TryReply(session, command.Name!, command.IsAccepted, command.CommandSequence);
            case InboundEventKind.PartyLeave:
                return m_parties.TryLeave(session, command.CommandSequence);
            case InboundEventKind.PartyKick:
                return m_parties.TryKick(session, command.Name!, command.CommandSequence);
            case InboundEventKind.PartyLead:
                return m_parties.TryLead(session, command.Name!, command.CommandSequence);
        }

        if (player.IsDead)
        {
            return command.Kind == InboundEventKind.Respawn && m_life.TryRespawn(session, tick)
                ? CommandRejectionReason.None
                : CommandRejectionReason.NotAllowedNow;
        }

        return command.Kind switch
        {
            InboundEventKind.Attack => m_targeting.TryAttack(session, command.Target)
                ? CommandRejectionReason.None
                : CommandRejectionReason.InvalidTarget,
            InboundEventKind.Cancel => Cancel(player),
            InboundEventKind.UseSkill => ReasonFor(
                m_combat.TryBeginCast(session.Map!, player, command.Skill, command.Target, tick)),
            InboundEventKind.Logout => TryLogout(session, command.CommandSequence),
            InboundEventKind.Pickup => m_pickups.TryStart(session, command.Target, command.CommandSequence, tick),
            InboundEventKind.Equip => m_items.TryEquip(session, command.InventoryItem, command.CommandSequence),
            InboundEventKind.Unequip => m_items.TryUnequip(session, command.Slot, command.CommandSequence),
            InboundEventKind.UseItem => m_items.TryUse(session, command.InventoryItem, command.CommandSequence),
            InboundEventKind.Buy => m_items.TryBuy(
                session,
                command.Target,
                command.Item,
                command.Quantity,
                command.CommandSequence),
            InboundEventKind.Sell => m_items.TrySell(
                session,
                command.Target,
                command.InventoryItem,
                command.Quantity,
                command.CommandSequence),
            InboundEventKind.AcceptQuest => m_progression.TryAcceptQuest(session, command.Target, command.Quest),
            InboundEventKind.CompleteQuest => m_items.TryCompleteQuest(
                session,
                command.Target,
                command.Quest,
                command.CommandSequence),
            InboundEventKind.AllocateStat => m_builds.TryRaise(
                player,
                session.Connection,
                command.Stat,
                (int)command.Quantity),
            InboundEventKind.LearnSkill => Learn(session, command.Skill),
            InboundEventKind.ResetBuild => ResetBuild(session, command.Target),
            InboundEventKind.ChangeJob => m_items.TryChangeJob(
                session,
                command.Target,
                command.Job,
                command.CommandSequence),
            _ => CommandRejectionReason.NotAllowedNow
        };
    }

    // The Guildmaster's reset (Gameplay Systems §6.1): refused while an inventory operation is in flight, as a turn-in
    // is, then by the NPC checks. A cast under way ends, status effects run out unchanged, and the checkpoint is queued
    // at once, even with nothing spent.
    private CommandRejectionReason ResetBuild(ClientSession session, EntityId npc)
    {
        CharacterSession character = session.Character!;
        if (character.Operation != null)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? found);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!found!.Definition.OffersReset)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        m_combat.InterruptCast(session.Player!);
        m_builds.ResetAtGuildmaster(session.Player!, session.Connection);
        session.NeedsSkillList = true;
        m_lifetime.QueueCheckpoint(character);
        return CommandRejectionReason.None;
    }

    // The owner hears of the new level through the skill list, and of the point spent through the sheet.
    private CommandRejectionReason Learn(ClientSession session, SkillDefinitionId skill)
    {
        CommandRejectionReason refusal = m_builds.TryLearn(session.Player!, session.Connection, skill);
        if (refusal == CommandRejectionReason.None)
        {
            session.NeedsSkillList = true;
        }

        return refusal;
    }

    private void Reject(ClientSession session, uint commandSequence, CommandRejectionReason reason)
    {
        session.RefusedCommands++;
        m_sender.Send(session.Connection, new CommandRejected(commandSequence, reason));
    }

    private static CommandRejectionReason ReasonFor(CastRefusal refusal)
    {
        return refusal switch
        {
            CastRefusal.None => CommandRejectionReason.None,
            CastRefusal.NotEnoughSp => CommandRejectionReason.NotEnoughSp,
            CastRefusal.InvalidTarget => CommandRejectionReason.InvalidTarget,
            CastRefusal.OutOfRange => CommandRejectionReason.OutOfRange,
            _ => CommandRejectionReason.NotAllowedNow
        };
    }

    // Stops the auto-attack and interrupts a cast; a swing already begun still resolves (Gameplay Systems §7, §9).
    private CommandRejectionReason Cancel(PlayerEntity player)
    {
        player.Combat.IsAutoAttacking = false;
        m_combat.InterruptCast(player);
        return CommandRejectionReason.None;
    }

    // Logout stops new commands, ends a cast in progress, waits for an inventory operation in flight, and writes the
    // final checkpoint; only once it is written does the character leave (Persistence §7). A cast left to resolve
    // after the checkpoint was taken would spend SP and heal or hurt without being saved. Without the database the
    // logout is refused and the player stays.
    private CommandRejectionReason TryLogout(ClientSession session, uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (!m_persistence.IsAvailable)
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        character.IsLoggingOut = true;
        character.LogoutSequence = commandSequence;
        character.Player.Combat.IsAutoAttacking = false;
        m_combat.InterruptCast(character.Player);
        // Moves queued before the logout must not walk the character away from the checkpoint it is about to write.
        session.Input!.Halt();
        if (character.Operation == null)
        {
            QueueLogoutCheckpoint(session, character);
        }

        return CommandRejectionReason.None;
    }

    private void OnOperationSettled(CharacterSession character)
    {
        if (character.IsLoggingOut && character.LogoutCheckpoint == null && character.Connection != null)
        {
            QueueLogoutCheckpoint(character.Connection, character);
        }
    }

    private void QueueLogoutCheckpoint(ClientSession session, CharacterSession character)
    {
        ConnectionId connection = session.Connection;
        PersistenceJob? checkpoint = null;
        checkpoint = m_lifetime.QueueCheckpoint(
            character,
            outcome => CompleteLogout(connection, character, checkpoint!, outcome));
        character.LogoutCheckpoint = checkpoint;
    }

    // A checkpoint that met an outage goes back into its slot and completes again later, possibly after a newer
    // logout began; only this logout's own checkpoint may finish it.
    private void CompleteLogout(
        ConnectionId connection,
        CharacterSession character,
        PersistenceJob checkpoint,
        PersistenceOutcome outcome)
    {
        if (!character.IsLoggingOut
            || character.LogoutCheckpoint != checkpoint
            || !m_sessions.TryGet(connection, out ClientSession? session)
            || session == null
            || session.Character != character)
        {
            return;
        }

        if (outcome != PersistenceOutcome.Succeeded)
        {
            CancelLogout(session, character);
            return;
        }

        m_lifetime.Remove(character);
        session.Input = null;
        session.KnownEntities.Clear();
        session.State = SessionState.Authenticated;
        m_sender.Send(connection, new LogoutComplete());
        QueueCharacterList(session);
    }

    private void CancelLogout(ClientSession session, CharacterSession character)
    {
        character.IsLoggingOut = false;
        character.LogoutCheckpoint = null;
        m_audit.CommandRefused(session, InboundEventKind.Logout, CommandRejectionReason.ServiceUnavailable);
        Reject(session, character.LogoutSequence, CommandRejectionReason.ServiceUnavailable);
    }

    // A logout still waiting when the database stops answering, for its checkpoint or for a pickup before it, would
    // hold the player still for the whole outage; it is cancelled instead and the player plays on (Persistence §7).
    // A checkpoint it queued stays queued and is written later like any other.
    private void CancelLogoutsWhileUnavailable()
    {
        if (m_persistence.IsAvailable)
        {
            return;
        }

        m_cancelledLogouts.Clear();
        foreach (CharacterSession character in m_sessions.Characters)
        {
            if (character.IsLoggingOut && character.Connection != null)
            {
                m_cancelledLogouts.Add(character);
            }
        }

        foreach (CharacterSession character in m_cancelledLogouts)
        {
            ClientSession session = character.Connection!;
            try
            {
                CancelLogout(session, character);
            }
            catch (Exception exception)
            {
                CloseFaulted(session.Connection, exception);
            }
        }
    }

    // One peer's input or result must never take the tick down with it.
    private void CloseFaulted(ConnectionId connection, Exception exception)
    {
        if (m_sessions.TryGet(connection, out ClientSession? faulted) && faulted != null)
        {
            LogSessionFaulted(m_logger, connection.Value, AccountOf(faulted), CharacterOf(faulted), exception);
            Close(faulted, DisconnectReason.InternalError);
            return;
        }

        LogSessionFaulted(m_logger, connection.Value, 0, 0, exception);
    }

    private void ExpireSilentConnections(uint tick)
    {
        m_expired.Clear();
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.AwaitingHello &&
                tick - session.ConnectedAtTick >= m_handshakeTimeoutTicks)
            {
                m_expired.Add(session);
            }
        }

        foreach (ClientSession session in m_expired)
        {
            Refuse(session, DisconnectReason.AuthenticationFailed);
        }
    }

    // Layer 2 of the abuse controls (Network Protocol §11). A command without a sequence that is throttled is
    // simply dropped: it is counted and scored here and nothing answers it.
    private bool IsThrottled(ClientSession session, InboundEventKind kind)
    {
        if (session.CommandLimits == null || session.CommandLimits.TryTake(kind, m_currentTick, out string limit))
        {
            return false;
        }

        session.ThrottledCommands++;
        ThrottledCommands++;
        m_instruments.RecordRateLimited(limit);
        m_audit.CommandThrottled(session, kind, limit);
        Score(session, Violation.CommandRate, 1);
        return true;
    }

    // With the limits off a connection has no score, and nothing is scored.
    private void Score(ClientSession session, Violation violation, int count)
    {
        if (session.Violations == null || count <= 0)
        {
            return;
        }

        session.Violations.Add(violation, count, m_currentTick);
        Violations += count;
        m_instruments.RecordViolations(violation, count);
        m_audit.ViolationScored(session, violation, session.Violations.Value);
    }

    // A disconnect for violations or rate excess (Network Protocol §3, §11). The character is checkpointed and removed
    // at once, as after a logout, and the account, or the address when the connection never signed in, is refused
    // until the cooldown is over: a kick after sign-in then shuts out no one else behind the same carrier NAT.
    private void Expel(ClientSession session, DisconnectReason reason)
    {
        ViolationDisconnects++;
        m_instruments.RecordViolationDisconnect(reason);
        m_audit.ViolationDisconnect(session, reason, session.Violations?.Value ?? 0d);
        if (session.Character != null)
        {
            session.Character.IsExpelled = true;
        }

        if (session.Account is AccountId account)
        {
            m_cooldowns.Start(account, m_currentTick);
        }
        else
        {
            m_sender.CoolDownAddress(session.Connection);
        }

        Close(session, reason);
    }

    private bool IsOpen(ClientSession session)
    {
        return m_sessions.TryGet(session.Connection, out ClientSession? open) && ReferenceEquals(open, session);
    }

    private void Refuse(ClientSession session, DisconnectReason reason)
    {
        if (reason == DisconnectReason.AuthenticationFailed || reason == DisconnectReason.SessionExpired)
        {
            AuthenticationFailures++;
            m_instruments.RecordAuthenticationFailure(reason);
        }

        LogHandshakeRejected(m_logger, session.Connection.Value, AccountOf(session), reason, null);
        Close(session, reason);
    }

    private void Close(ClientSession session, DisconnectReason reason)
    {
        Remove(session);
        m_sender.Disconnect(session.Connection, reason);
    }

    private void Remove(ClientSession session)
    {
        // Read before detaching, which unlinks the character from the session.
        long characterId = CharacterOf(session);
        CharacterSession? character = session.Character;
        if (character != null)
        {
            // A player who asked to leave, or was disconnected for violations, is not kept for a reconnect.
            if (character.IsLoggingOut || character.IsExpelled)
            {
                m_lifetime.CheckpointAndRemove(character);
            }
            else
            {
                m_lifetime.Detach(character, m_currentTick);
            }
        }

        m_sessions.Remove(session);
        LogSessionClosed(m_logger, session.Connection.Value, AccountOf(session), characterId, null);
    }

    // Correlation fields for the logs (System Architecture §10); 0 when the connection has not got that far.
    private static long AccountOf(ClientSession session)
    {
        return session.Account?.Value ?? 0;
    }

    private static long CharacterOf(ClientSession session)
    {
        return session.Character?.Character.Value ?? 0;
    }
}
}
