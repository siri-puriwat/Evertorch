using System;
using System.Collections.Generic;
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

    private static readonly Action<ILogger, long, DisconnectReason, Exception?> LogHandshakeRejected =
        LoggerMessage.Define<long, DisconnectReason>(
            LogLevel.Information,
            new EventId(2001, "HandshakeRejected"),
            "Connection {Connection} was refused: {Reason}.");

    private static readonly Action<ILogger, long, long, long, Exception?> LogWorldEntered =
        LoggerMessage.Define<long, long, long>(
            LogLevel.Information,
            new EventId(2002, "WorldEntered"),
            "Connection {Connection} entered the world as character {Character}, entity {Entity}.");

    private static readonly Action<ILogger, long, Exception?> LogSessionClosed =
        LoggerMessage.Define<long>(
            LogLevel.Information,
            new EventId(2003, "SessionClosed"),
            "Connection {Connection} closed.");

    private static readonly Action<ILogger, long, long, Exception?> LogAuthenticated =
        LoggerMessage.Define<long, long>(
            LogLevel.Information,
            new EventId(2005, "SessionAuthenticated"),
            "Connection {Connection} signed in to account {Account}.");

    private static readonly Action<ILogger, long, Exception?> LogSessionFaulted =
        LoggerMessage.Define<long>(
            LogLevel.Error,
            new EventId(2004, "SessionFaulted"),
            "Handling input from connection {Connection} failed; the connection was closed.");

    private readonly InboundQueue m_inbound;
    private readonly PersistenceWorker m_persistence;
    private readonly SessionRegistry m_sessions;
    private readonly HandshakeValidator m_handshake;
    private readonly ISessionTokenValidator m_tokens;
    private readonly WorldSimulation m_world;
    private readonly MessageSender m_sender;
    private readonly Targeting m_targeting;
    private readonly PlayerLife m_life;
    private readonly TimeProvider m_time;
    private readonly ILogger<SessionManager> m_logger;
    private readonly string m_serverBuildVersion;
    private readonly uint m_tickRate;
    private readonly uint m_handshakeTimeoutTicks;
    private readonly int m_maxQueuedInputs;
    private readonly List<ClientSession> m_expired = new();

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
        TimeProvider time,
        IOptions<SimulationOptions> simulation,
        IOptions<NetworkOptions> network,
        IOptions<CompatibilityOptions> compatibility,
        IOptions<WorldOptions> worldOptions,
        ILogger<SessionManager> logger)
    {
        m_maxQueuedInputs = worldOptions.Value.MaxQueuedInputs;
        m_inbound = inbound;
        m_persistence = persistence;
        m_sessions = sessions;
        m_handshake = handshake;
        m_tokens = tokens;
        m_world = world;
        m_sender = sender;
        m_targeting = targeting;
        m_life = life;
        m_time = time;
        m_logger = logger;
        m_serverBuildVersion = compatibility.Value.ServerBuildVersion;
        m_tickRate = (uint)simulation.Value.TickRate;

        long timeoutTicks = (long)network.Value.HandshakeTimeoutMs * simulation.Value.TickRate /
            MillisecondsPerSecond;
        m_handshakeTimeoutTicks = (uint)Math.Max(1L, timeoutTicks);
    }

    /// <summary>
    ///     Inputs that arrived malformed, out of order for the session's state, or for a connection already closed.
    /// </summary>
    public long IgnoredEvents { get; private set; }

    public TickPhase Phase => TickPhase.DrainCommands;

    public void Execute(in TickContext context)
    {
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
                LogSessionFaulted(m_logger, job.Connection.Value, exception);
                if (m_sessions.TryGet(job.Connection, out ClientSession? faulted) && faulted != null)
                {
                    Close(faulted, DisconnectReason.InternalError);
                }
            }
        }

        while (m_inbound.TryDequeue(out InboundEvent inboundEvent))
        {
            try
            {
                Handle(inboundEvent, context.Tick);
            }
            catch (Exception exception)
            {
                // One peer's input must never take the tick down with it.
                LogSessionFaulted(m_logger, inboundEvent.Connection.Value, exception);
                if (m_sessions.TryGet(inboundEvent.Connection, out ClientSession? faulted) && faulted != null)
                {
                    Close(faulted, DisconnectReason.InternalError);
                }
            }
        }

        ExpireSilentConnections(context.Tick);
    }

    private void Handle(InboundEvent inboundEvent, uint tick)
    {
        if (inboundEvent.Kind == InboundEventKind.Connected)
        {
            m_sessions.Add(new ClientSession(inboundEvent.Connection, tick));
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
                HandleMove(session, inboundEvent.Intent);
                break;
            case InboundEventKind.Target:
                HandleTarget(session, inboundEvent.Target);
                break;
            case InboundEventKind.Attack:
            case InboundEventKind.Cancel:
            case InboundEventKind.Respawn:
                HandleCommand(session, inboundEvent, tick);
                break;
            default:
                IgnoredEvents++;
                break;
        }
    }

    private void HandleHello(ClientSession session, ClientHello hello)
    {
        if (session.State != SessionState.AwaitingHello)
        {
            IgnoredEvents++;
            return;
        }

        DisconnectReason refusal = m_handshake.Validate(hello);
        if (refusal != DisconnectReason.None)
        {
            LogHandshakeRejected(m_logger, session.Connection.Value, refusal, null);
            Close(session, refusal);
            return;
        }

        // Admission needs the database (Persistence §9): without it the connection is refused as not ready.
        string token = hello.SessionToken;
        ConnectionId connection = session.Connection;
        var authentication = new PersistenceJob<AccountId?>(
            "authenticate",
            connection,
            0,
            (store, cancellation) => m_tokens.ValidateAsync(token, store, cancellation),
            (outcome, account) => CompleteAuthentication(connection, outcome, account));
        if (!m_persistence.TryEnqueue(authentication))
        {
            LogHandshakeRejected(m_logger, connection.Value, DisconnectReason.ServerNotReady, null);
            Close(session, DisconnectReason.ServerNotReady);
            return;
        }

        session.State = SessionState.Authenticating;
    }

    private void CompleteAuthentication(ConnectionId connection, PersistenceOutcome outcome, AccountId? account)
    {
        // Connection numbers are never reused, so a session found here is the one that asked.
        if (!m_sessions.TryGet(connection, out ClientSession? session)
            || session == null
            || session.State != SessionState.Authenticating)
        {
            return;
        }

        DisconnectReason refusal = outcome != PersistenceOutcome.Succeeded ? DisconnectReason.ServerNotReady
            : account == null ? DisconnectReason.AuthenticationFailed
            : DisconnectReason.None;
        if (refusal != DisconnectReason.None)
        {
            LogHandshakeRejected(m_logger, connection.Value, refusal, null);
            Close(session, refusal);
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
        if (!m_persistence.TryEnqueue(list))
        {
            LogHandshakeRejected(m_logger, connection.Value, DisconnectReason.ServerNotReady, null);
            Close(session, DisconnectReason.ServerNotReady);
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
            LogHandshakeRejected(m_logger, connection.Value, DisconnectReason.ServerNotReady, null);
            Close(session, DisconnectReason.ServerNotReady);
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
            LogHandshakeRejected(m_logger, session.Connection.Value, DisconnectReason.AuthenticationFailed, null);
            Close(session, DisconnectReason.AuthenticationFailed);
            return;
        }

        // A character of another account is refused exactly like one that does not exist (Network Protocol §4).
        if (session.State != SessionState.Authenticated || !Owns(session, request.Character))
        {
            IgnoredEvents++;
            return;
        }

        if (m_sessions.TryGetByCharacter(request.Character, out ClientSession? previous) && previous != null)
        {
            Close(previous, DisconnectReason.SessionReplaced);
        }

        PlayerEntity player = m_world.SpawnPlayer(request.Character, session.Connection, out MapInstance map);
        session.Player = player;
        session.Map = map;
        session.Input = new PlayerInputState(m_maxQueuedInputs);
        session.State = SessionState.InWorld;
        m_sessions.BindCharacter(session, request.Character);

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
                player.AttackRange));
        LogWorldEntered(m_logger, session.Connection.Value, request.Character.Value, player.Id.Value, null);
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
    private void HandleMove(ClientSession session, MoveIntent intent)
    {
        if (session.State != SessionState.InWorld || session.Input == null)
        {
            IgnoredEvents++;
            return;
        }

        session.Input.Queue.TryEnqueue(intent);
    }

    private void HandleTarget(ClientSession session, EntityId target)
    {
        if (session.State != SessionState.InWorld)
        {
            IgnoredEvents++;
            return;
        }

        if (session.Player?.IsDead == true || !m_targeting.TrySelect(session, target))
        {
            session.RefusedCommands++;
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
            return;
        }

        session.LastCommandSequence = command.CommandSequence;
        bool isAccepted = session.Player.IsDead
            ? command.Kind == InboundEventKind.Respawn && m_life.TryRespawn(session, tick)
            : command.Kind switch
            {
                InboundEventKind.Attack => m_targeting.TryAttack(session, command.Target),
                InboundEventKind.Cancel => Cancel(session.Player),
                _ => false
            };

        if (!isAccepted)
        {
            session.RefusedCommands++;
        }
    }

    private static bool Cancel(PlayerEntity player)
    {
        player.Combat.IsAutoAttacking = false;
        return true;
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
            LogHandshakeRejected(m_logger, session.Connection.Value, DisconnectReason.AuthenticationFailed, null);
            Close(session, DisconnectReason.AuthenticationFailed);
        }
    }

    private void Close(ClientSession session, DisconnectReason reason)
    {
        Remove(session);
        m_sender.Disconnect(session.Connection, reason);
    }

    private void Remove(ClientSession session)
    {
        if (session.Player != null && session.Map != null)
        {
            m_world.RemovePlayer(session.Map, session.Player);
        }

        m_sessions.Remove(session);
        LogSessionClosed(m_logger, session.Connection.Value, null);
    }
}
}
