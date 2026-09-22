using System;
using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     A development wrapper that makes a good connection behave like a bad one: every message is delayed, and
///     unreliable messages are also dropped and reordered. Reliable messages keep their order, as the real transport
///     guarantees. The same seed and the same clock readings give the same outcome.
/// </summary>
public sealed class LossyTransport : IClientTransport
{
    // Longer than two snapshot intervals, so a held message really is overtaken.
    private const double ReorderDelaySeconds = 0.12;

    private readonly IClientTransport m_inner;
    private readonly Func<double> m_nowSeconds;
    private readonly Random m_random;
    private readonly List<Held> m_outbound = new();
    private readonly List<Held> m_inbound = new();
    private readonly List<Held> m_due = new();
    private readonly Capture m_capture;
    private double m_lastReliableOutbound;
    private double m_lastReliableInbound;
    private long m_order;

    public LossyTransport(IClientTransport inner, int seed, Func<double> nowSeconds)
    {
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        m_nowSeconds = nowSeconds ?? throw new ArgumentNullException(nameof(nowSeconds));
        m_random = new Random(seed);
        m_capture = new Capture(this);
    }

    /// <summary>
    ///     One-way delay added in each direction.
    /// </summary>
    public int LatencyMilliseconds { get; set; }

    public int JitterMilliseconds { get; set; }

    public int LossPercent { get; set; }

    /// <summary>
    ///     Chance that an unreliable message is held back long enough for later ones to overtake it.
    /// </summary>
    public int ReorderPercent { get; set; }

    public int Dropped { get; private set; }

    public int Reordered { get; private set; }

    // With nothing to simulate and nothing waiting, the wrapper must cost nothing: no extra frame either way.
    private bool IsTransparent =>
        LatencyMilliseconds == 0 && JitterMilliseconds == 0 && LossPercent == 0 && ReorderPercent == 0;

    public bool IsConnected => m_inner.IsConnected;

    public int RoundTripMilliseconds => m_inner.RoundTripMilliseconds + 2 * LatencyMilliseconds;

    public void Connect(string host, int port)
    {
        m_inner.Connect(host, port);
    }

    public void Disconnect()
    {
        m_outbound.Clear();
        m_inbound.Clear();
        m_inner.Disconnect();
    }

    public void Send(ProtocolChannel channel, MessageDelivery delivery, ReadOnlySpan<byte> payload)
    {
        if (IsTransparent && m_outbound.Count == 0)
        {
            m_inner.Send(channel, delivery, payload);
            return;
        }

        Hold(m_outbound, ref m_lastReliableOutbound, m_nowSeconds(), channel, delivery, payload);
    }

    public void Poll(IClientTransportListener listener)
    {
        if (listener == null)
        {
            throw new ArgumentNullException(nameof(listener));
        }

        double now = m_nowSeconds();
        TakeDue(m_outbound, now);
        foreach (Held held in m_due)
        {
            m_inner.Send(held.Channel, held.Delivery, held.Payload);
        }

        m_capture.Listener = listener;
        m_capture.Now = now;
        m_inner.Poll(m_capture);
        m_capture.Listener = null;

        TakeDue(m_inbound, now);
        foreach (Held held in m_due)
        {
            listener.OnPayload(held.Channel, held.Payload);
        }

        m_due.Clear();
    }

    private void Hold(
        List<Held> queue,
        ref double lastReliable,
        double now,
        ProtocolChannel channel,
        MessageDelivery delivery,
        ReadOnlySpan<byte> payload)
    {
        double release = now + LatencyMilliseconds / 1000.0;
        if (delivery == MessageDelivery.ReliableOrdered)
        {
            release = Math.Max(release, lastReliable);
            lastReliable = release;
        }
        else
        {
            if (m_random.Next(100) < LossPercent)
            {
                Dropped++;
                return;
            }

            release += m_random.NextDouble() * JitterMilliseconds / 1000.0;
            if (m_random.Next(100) < ReorderPercent)
            {
                Reordered++;
                release += ReorderDelaySeconds;
            }
        }

        queue.Add(new Held(release, m_order++, channel, delivery, payload.ToArray()));
    }

    private void TakeDue(List<Held> queue, double now)
    {
        m_due.Clear();
        for (int index = queue.Count - 1; index >= 0; index--)
        {
            if (queue[index].Release <= now)
            {
                m_due.Add(queue[index]);
                queue.RemoveAt(index);
            }
        }

        m_due.Sort(Held.ByReleaseThenOrder);
    }

    private sealed class Held
    {
        public static readonly Comparison<Held> ByReleaseThenOrder = (left, right) =>
        {
            int byRelease = left.Release.CompareTo(right.Release);
            return byRelease != 0 ? byRelease : left.Order.CompareTo(right.Order);
        };

        public Held(double release, long order, ProtocolChannel channel, MessageDelivery delivery, byte[] payload)
        {
            Release = release;
            Order = order;
            Channel = channel;
            Delivery = delivery;
            Payload = payload;
        }

        public double Release { get; }

        public long Order { get; }

        public ProtocolChannel Channel { get; }

        public MessageDelivery Delivery { get; }

        public byte[] Payload { get; }
    }

    private sealed class Capture : IClientTransportListener
    {
        private readonly LossyTransport m_owner;

        public Capture(LossyTransport owner)
        {
            m_owner = owner;
        }

        public IClientTransportListener? Listener { get; set; }

        public double Now { get; set; }

        public void OnConnected()
        {
            Listener?.OnConnected();
        }

        public void OnDisconnected(TransportDisconnectCause cause, ReadOnlySpan<byte> notice)
        {
            m_owner.m_outbound.Clear();
            m_owner.m_inbound.Clear();
            Listener?.OnDisconnected(cause, notice);
        }

        public void OnPayload(ProtocolChannel channel, ReadOnlySpan<byte> payload)
        {
            MessageDelivery delivery = MessageDelivery.ReliableOrdered;
            if (MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode))
            {
                MessageRouting.TryGetRoute(opcode, out ProtocolChannel _, out delivery);
            }

            // Stamped with the poll's own time, so a message with nothing to wait for is released by this poll.
            m_owner.Hold(m_owner.m_inbound, ref m_owner.m_lastReliableInbound, Now, channel, delivery, payload);
        }
    }
}
}
