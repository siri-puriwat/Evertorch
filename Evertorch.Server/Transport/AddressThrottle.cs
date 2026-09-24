using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Per remote address, a rate of connection requests and a cap on connections held at once (Network Protocol §11).
///     Both are generous by default, because many phones share one address behind carrier NAT. An address whose
///     connection was closed for violations before it signed in also waits out a re-admission cooldown. The table of
///     addresses is bounded: when it is full, addresses with no connection, a full budget, and no cooldown make room,
///     and if none can, the request is refused.
/// </summary>
public sealed class AddressThrottle
{
    private readonly object m_gate = new();
    private readonly Dictionary<IPAddress, Entry> m_addresses = new();
    private readonly List<IPAddress> m_idle = new();
    private readonly IMonotonicClock m_clock;
    private readonly bool m_isEnabled;
    private readonly double m_requestsPerSecond;
    private readonly int m_maxConnections;
    private readonly int m_maxAddresses;
    private readonly TimeSpan m_cooldown;

    public AddressThrottle(IOptions<AbuseOptions> options, IMonotonicClock clock)
    {
        m_clock = clock;
        m_isEnabled = options.Value.Enabled;
        m_requestsPerSecond = options.Value.ConnectionRequestsPerSecond;
        m_maxConnections = options.Value.MaxConnectionsPerAddress;
        m_maxAddresses = options.Value.MaxTrackedAddresses;
        m_cooldown = TimeSpan.FromMilliseconds(options.Value.KickCooldownMs);
    }

    /// <summary>
    ///     False when a connection request from <paramref name="address" /> must be refused without a word;
    ///     <paramref name="limit" /> then names the limit it met (<see cref="ServerInstruments" />).
    /// </summary>
    public bool TryAdmit(IPAddress address, out string limit)
    {
        limit = string.Empty;
        if (!m_isEnabled)
        {
            return true;
        }

        TimeSpan now = m_clock.Elapsed;
        lock (m_gate)
        {
            if (!m_addresses.TryGetValue(address, out Entry? entry))
            {
                if (m_addresses.Count >= m_maxAddresses && !MakeRoom(now))
                {
                    limit = ServerInstruments.AddressRateLimit;
                    return false;
                }

                entry = new Entry(now, m_requestsPerSecond);
                m_addresses.Add(address, entry);
            }

            entry.Refill(now, m_requestsPerSecond);
            if (entry.Connections >= m_maxConnections)
            {
                limit = ServerInstruments.AddressConnectionsLimit;
                return false;
            }

            if (entry.Tokens < 1d)
            {
                limit = ServerInstruments.AddressRateLimit;
                return false;
            }

            entry.Tokens -= 1d;
            return true;
        }
    }

    public void OnConnected(IPAddress address)
    {
        lock (m_gate)
        {
            // A connection admitted before the limits applied, or while its entry made room, still counts.
            GetOrAdd(address).Connections++;
        }
    }

    /// <summary>
    ///     True while <paramref name="address" /> waits out a re-admission cooldown.
    /// </summary>
    public bool IsCoolingDown(IPAddress address)
    {
        if (!m_isEnabled)
        {
            return false;
        }

        TimeSpan now = m_clock.Elapsed;
        lock (m_gate)
        {
            return m_addresses.TryGetValue(address, out Entry? entry) && entry.CooldownEndsAt > now;
        }
    }

    /// <summary>
    ///     Refuses connection requests from <paramref name="address" /> for <c>Abuse:KickCooldownMs</c>.
    /// </summary>
    public void StartCooldown(IPAddress address)
    {
        if (!m_isEnabled || m_cooldown <= TimeSpan.Zero)
        {
            return;
        }

        lock (m_gate)
        {
            GetOrAdd(address).CooldownEndsAt = m_clock.Elapsed + m_cooldown;
        }
    }

    public void OnDisconnected(IPAddress address)
    {
        lock (m_gate)
        {
            if (m_addresses.TryGetValue(address, out Entry? entry) && entry.Connections > 0)
            {
                entry.Connections--;
            }
        }
    }

    private Entry GetOrAdd(IPAddress address)
    {
        if (!m_addresses.TryGetValue(address, out Entry? entry))
        {
            entry = new Entry(m_clock.Elapsed, m_requestsPerSecond);
            m_addresses.Add(address, entry);
        }

        return entry;
    }

    // Forgets every address that holds no connection, has not asked lately (its budget has refilled), and is not
    // cooling down.
    private bool MakeRoom(TimeSpan now)
    {
        m_idle.Clear();
        foreach (KeyValuePair<IPAddress, Entry> pair in m_addresses)
        {
            pair.Value.Refill(now, m_requestsPerSecond);
            if (pair.Value.Connections == 0
                && pair.Value.Tokens >= m_requestsPerSecond
                && pair.Value.CooldownEndsAt <= now)
            {
                m_idle.Add(pair.Key);
            }
        }

        foreach (IPAddress address in m_idle)
        {
            m_addresses.Remove(address);
        }

        return m_addresses.Count < m_maxAddresses;
    }

    private sealed class Entry
    {
        private TimeSpan m_refilledAt;

        public Entry(TimeSpan now, double tokens)
        {
            m_refilledAt = now;
            Tokens = tokens;
        }

        public double Tokens { get; set; }

        public int Connections { get; set; }

        public TimeSpan CooldownEndsAt { get; set; }

        // At most one second's worth of requests is saved up.
        public void Refill(TimeSpan now, double perSecond)
        {
            if (now > m_refilledAt)
            {
                Tokens = Math.Min(perSecond, Tokens + (now - m_refilledAt).TotalSeconds * perSecond);
                m_refilledAt = now;
            }
        }
    }
}
}
