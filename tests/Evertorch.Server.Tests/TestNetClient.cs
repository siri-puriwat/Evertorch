using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Evertorch.Protocol;
using LiteNetLib;
using DisconnectReason = LiteNetLib.DisconnectReason;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A bare LiteNetLib client for loopback tests. It knows nothing about the server's code, so what it observes is
///     what any real client would see on the wire.
/// </summary>
internal sealed class TestNetClient : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly EventBasedNetListener m_listener = new();
    private readonly NetManager m_manager;
    private NetPeer? m_peer;

    public TestNetClient()
    {
        m_manager = new NetManager(m_listener)
        {
            ChannelsCount = MessageRouting.ChannelCount,
            AutoRecycle = true,
            IPv6Enabled = false
        };
        m_listener.PeerConnectedEvent += _ => IsConnected = true;
        m_listener.PeerDisconnectedEvent += (_, info) =>
        {
            IsConnected = false;
            IsDisconnected = true;
            TransportReason = info.Reason;
            if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
            {
                DisconnectNotice.TryRead(info.AdditionalData.GetRemainingBytesSpan(), out DisconnectNotice? notice);
                Notice = notice;
            }
        };
        m_listener.NetworkReceiveEvent += (_, reader, channel, method) =>
            Received.Add(new ReceivedMessage(channel, method, reader.GetRemainingBytes()));
        m_manager.Start();
    }

    public bool IsConnected { get; private set; }

    public bool IsDisconnected { get; private set; }

    public DisconnectReason TransportReason { get; private set; }

    public DisconnectNotice? Notice { get; private set; }

    public List<ReceivedMessage> Received { get; } = new();

    public int MaxUnreliablePayload => m_peer!.GetMaxSinglePacketSize(DeliveryMethod.Sequenced);

    public void Dispose()
    {
        m_manager.Stop(false);
    }

    public void Connect(int port, string key)
    {
        m_peer = m_manager.Connect("127.0.0.1", port, key);
    }

    public void Send(byte[] payload, ProtocolChannel channel, DeliveryMethod method)
    {
        m_peer!.Send(payload, (byte)channel, method);
    }

    public void Disconnect()
    {
        m_peer!.Disconnect();
    }

    /// <summary>
    ///     Pumps events until the condition holds. Returns false on timeout so the test can assert with a clear message.
    /// </summary>
    public bool WaitFor(Func<bool> condition)
    {
        return WaitFor(condition, Timeout);
    }

    public bool WaitFor(Func<bool> condition, TimeSpan limit)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < limit)
        {
            m_manager.PollEvents();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return false;
    }

    internal sealed class ReceivedMessage
    {
        public ReceivedMessage(byte channel, DeliveryMethod method, byte[] payload)
        {
            Channel = channel;
            Method = method;
            Payload = payload;
        }

        public byte Channel { get; }

        public DeliveryMethod Method { get; }

        public byte[] Payload { get; }
    }
}
}
