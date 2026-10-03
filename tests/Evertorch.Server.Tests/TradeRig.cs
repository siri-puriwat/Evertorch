using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Characters entered on a <see cref="TestServer" /> who send the trade's commands, each with its own command
///     sequence, and read what the server told them of it.
/// </summary>
internal sealed class TradeRig
{
    private readonly Dictionary<ConnectionId, uint> m_sequences = new();

    public TradeRig(TestServer? server = null)
    {
        Server = server ?? new TestServer();
    }

    public TestServer Server { get; }

    public ConnectionId Enter(long character)
    {
        ConnectionId connection = Server.EnterWorld(character);
        m_sequences[connection] = 0;
        return connection;
    }

    /// <summary>
    ///     Enters character <paramref name="character" /> holding what <paramref name="prepare" /> stores for it, as
    ///     earlier sessions would have left it.
    /// </summary>
    public ConnectionId Enter(long character, Action<InMemoryGameStore> prepare)
    {
        ConnectionId connection = Server.Connect();
        Server.SignInWithCharacter(connection, character);
        prepare(Server.Store);
        Server.SendEnterWorld(connection, character);
        Server.TickUntil(() => Server.SessionOf(connection).State == SessionState.InWorld);
        m_sequences[connection] = 0;
        return connection;
    }

    public uint Next(ConnectionId connection)
    {
        m_sequences.TryGetValue(connection, out uint sequence);
        m_sequences[connection] = ++sequence;
        return sequence;
    }

    public uint Request(ConnectionId connection, string name)
    {
        uint sequence = Next(connection);
        Server.SendTradeRequest(connection, name, sequence);
        Server.Tick();
        return sequence;
    }

    public uint Reply(ConnectionId connection, string requester, bool isAccepted)
    {
        uint sequence = Next(connection);
        Server.SendTradeReply(connection, requester, isAccepted, sequence);
        Server.Tick();
        return sequence;
    }

    public uint Offer(ConnectionId connection, long row, uint quantity)
    {
        uint sequence = Next(connection);
        Server.SendTradeOffer(connection, row, quantity, sequence);
        Server.Tick();
        return sequence;
    }

    public uint Lock(ConnectionId connection)
    {
        uint sequence = Next(connection);
        Server.SendTradeLock(connection, sequence);
        Server.Tick();
        return sequence;
    }

    public uint Confirm(ConnectionId connection)
    {
        uint sequence = Next(connection);
        Server.SendTradeConfirm(connection, sequence);
        Server.Tick();
        return sequence;
    }

    public uint Cancel(ConnectionId connection)
    {
        uint sequence = Next(connection);
        Server.SendTradeCancel(connection, sequence);
        Server.Tick();
        return sequence;
    }

    /// <summary>
    ///     Requests from <paramref name="requester" />, named <paramref name="requesterName" />, and accepts at
    ///     <paramref name="partner" />, named <paramref name="partnerName" />: the trade is open.
    /// </summary>
    public void Open(ConnectionId requester, ConnectionId partner, string requesterName, string partnerName)
    {
        Request(requester, partnerName);
        Reply(partner, requesterName, true);
    }

    public (TradeEventKind Kind, string Name, CommandRejectionReason Reason)[] Events(ConnectionId connection)
    {
        return Server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.TradeEvent)
            .Select(message => TradeEvent.TryRead(message.Payload, out TradeEvent? heard) ? heard! : null!)
            .Select(heard => (heard.Kind, heard.Name, heard.Reason))
            .ToArray();
    }

    public TradeSide[] Sides(ConnectionId connection)
    {
        return Server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.TradeSide)
            .Select(message => TradeSide.TryRead(message.Payload, out TradeSide? side) ? side! : null!)
            .ToArray();
    }

    public (uint Sequence, CommandRejectionReason Reason)[] Refusals(ConnectionId connection)
    {
        return Server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return (rejected.CommandSequence, rejected.Reason);
            })
            .ToArray();
    }

    public CommandRejectionReason RefusalOf(ConnectionId connection, uint sequence)
    {
        return Refusals(connection).Where(refusal => refusal.Sequence == sequence)
            .Select(refusal => refusal.Reason)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Single();
    }
}
}
