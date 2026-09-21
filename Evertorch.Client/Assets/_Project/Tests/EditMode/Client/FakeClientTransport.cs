using System;
using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Client.Tests.EditMode
{
internal sealed class FakeClientTransport : IClientTransport
{
    private readonly Queue<Action<IClientTransportListener>> m_events = new Queue<Action<IClientTransportListener>>();

    public bool IsConnected { get; private set; }

    public int RoundTripMilliseconds => 30;

    public string Host { get; private set; } = string.Empty;

    public int Port { get; private set; }

    public int DisconnectCalls { get; private set; }

    public List<SentMessage> Sent { get; } = new List<SentMessage>();

    public void Connect(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public void Disconnect()
    {
        DisconnectCalls++;
        IsConnected = false;
        m_events.Enqueue(
            listener => listener.OnDisconnected(TransportDisconnectCause.ClosedLocally, ReadOnlySpan<byte>.Empty));
    }

    public void Send(ProtocolChannel channel, MessageDelivery delivery, ReadOnlySpan<byte> payload)
    {
        Sent.Add(new SentMessage(channel, delivery, payload.ToArray()));
    }

    public void Poll(IClientTransportListener listener)
    {
        while (m_events.Count > 0)
        {
            m_events.Dequeue()(listener);
        }
    }

    public void CompleteConnect()
    {
        IsConnected = true;
        m_events.Enqueue(listener => listener.OnConnected());
    }

    public void Deliver(ProtocolChannel channel, byte[] payload)
    {
        m_events.Enqueue(listener => listener.OnPayload(channel, payload));
    }

    public void DropConnection(TransportDisconnectCause cause, byte[] notice)
    {
        IsConnected = false;
        m_events.Enqueue(listener => listener.OnDisconnected(cause, notice));
    }

    internal sealed class SentMessage
    {
        public SentMessage(ProtocolChannel channel, MessageDelivery delivery, byte[] payload)
        {
            Channel = channel;
            Delivery = delivery;
            Payload = payload;
        }

        public ProtocolChannel Channel { get; }

        public MessageDelivery Delivery { get; }

        public byte[] Payload { get; }
    }
}
}
