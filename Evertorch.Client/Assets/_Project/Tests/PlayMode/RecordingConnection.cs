using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A connection in the world over a transport that records what the client sends and delivers only what the test
///     gives it, so an idle <see cref="GameClient" /> can be seen to send a command (finding C7 of the Milestone 9
///     review).
/// </summary>
internal sealed class RecordingConnection : IClientTransport, IMapProvider
{
    private readonly Queue<Action<IClientTransportListener>> m_events = new();
    private readonly NavigationGrid m_grid;

    private RecordingConnection(NavigationGrid grid)
    {
        m_grid = grid;
        Connection = new ClientConnection(
            this,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, "0000000000000000", "dev:recording"),
            this);
    }

    public ClientConnection Connection { get; }

    /// <summary>
    ///     What the client sent after it entered the world, each payload whole.
    /// </summary>
    public List<byte[]> Sent { get; } = new();

    public bool IsConnected { get; private set; }

    public int RoundTripMilliseconds => 0;

    public void Connect(string host, int port)
    {
    }

    public void Disconnect()
    {
        IsConnected = false;
    }

    public void Send(ProtocolChannel channel, MessageDelivery delivery, ReadOnlySpan<byte> payload)
    {
        Sent.Add(payload.ToArray());
    }

    public void Poll(IClientTransportListener listener)
    {
        while (m_events.Count > 0)
        {
            m_events.Dequeue()(listener);
        }
    }

    public void Dispose()
    {
    }

    public bool TryGetNavigation(MapDefinitionId map, out NavigationGrid? grid)
    {
        grid = m_grid;
        return true;
    }

    /// <summary>
    ///     Gives <paramref name="client" /> a connection that entered the world as <paramref name="entered" /> says, on
    ///     <paramref name="grid" />, and that world as the client's own; returns the recording.
    /// </summary>
    public static RecordingConnection EnterWorld(GameClient client, WorldEntered entered, NavigationGrid grid)
    {
        var recording = new RecordingConnection(grid);
        ClientConnection connection = recording.Connection;
        connection.Connect("127.0.0.1", 7777);
        recording.IsConnected = true;
        recording.m_events.Enqueue(listener => listener.OnConnected());
        connection.Poll();
        var hello = new ServerHello(ProtocolConstants.ProtocolVersion, ProtocolConstants.BuildVersion, 0, 20, 0);
        byte[] helloBytes = new byte[hello.GetEncodedLength()];
        hello.Write(helloBytes);
        recording.Deliver(helloBytes);
        var list = new CharacterList(
            new[] { new CharacterListEntry(entered.Character, "Ann0", entered.Job, entered.Level) });
        byte[] listBytes = new byte[list.GetEncodedLength()];
        list.Write(listBytes);
        recording.Deliver(listBytes);
        connection.EnterWorld(entered.Character);
        byte[] enteredBytes = new byte[entered.GetEncodedLength()];
        entered.Write(enteredBytes);
        recording.Deliver(enteredBytes);
        recording.Sent.Clear();
        typeof(GameClient).GetProperty(nameof(GameClient.Connection))!.SetValue(client, connection);
        typeof(GameClient)
            .GetField("m_world", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, connection.World);
        return recording;
    }

    /// <summary>
    ///     The commands of <paramref name="opcode" /> the client sent, in order.
    /// </summary>
    public IEnumerable<byte[]> SentOf(MessageOpcode opcode)
    {
        return Sent.Where(payload => payload.Length >= 2 && BitConverter.ToUInt16(payload, 0) == (ushort)opcode);
    }

    private void Deliver(byte[] payload)
    {
        m_events.Enqueue(listener => listener.OnPayload(ProtocolChannel.Control, payload));
        Connection.Poll();
    }
}
}
