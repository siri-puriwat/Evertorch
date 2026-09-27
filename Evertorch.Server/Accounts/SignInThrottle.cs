using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The sign-in limits (Network Protocol §11): a rate per remote address, and a budget of failures per login,
///     whether or not an account has it, so no answer tells whether a login exists. Anyone may slow a login's
///     sign-ins this way; nobody can lock its account. Both tables are bounded. Safe on any thread.
/// </summary>
public sealed class SignInThrottle
{
    private readonly bool m_isEnabled;
    private readonly BucketTable<IPAddress> m_addresses;
    private readonly BucketTable<string> m_logins;

    public SignInThrottle(IOptions<AbuseOptions> options, IMonotonicClock clock)
    {
        AbuseOptions abuse = options.Value;
        m_isEnabled = abuse.Enabled;
        m_addresses = new BucketTable<IPAddress>(
            clock,
            abuse.SignInsPerSecond,
            abuse.SignInBurst,
            abuse.MaxTrackedAddresses,
            false);
        m_logins = new BucketTable<string>(
            clock,
            abuse.SignInFailuresPerMinute / 60d,
            abuse.SignInFailureBurst,
            abuse.MaxTrackedLogins,
            true);
    }

    public int TrackedLogins => m_logins.Count;

    /// <summary>
    ///     Spends one of the address's sign-ins; false when it has none left, or when the table is full of addresses
    ///     still spending theirs.
    /// </summary>
    public bool TryAdmitAddress(IPAddress address)
    {
        return !m_isEnabled || m_addresses.TryTake(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }

    /// <summary>
    ///     Whether the login may try its password now: false once its failures have spent its budget.
    /// </summary>
    public bool MayTry(string login)
    {
        return !m_isEnabled || m_logins.HasToken(login);
    }

    public void RecordFailure(string login)
    {
        if (m_isEnabled)
        {
            m_logins.Spend(login);
        }
    }

    // Token buckets by key. A key without an entry has a full bucket. When the table is full, full buckets are forgotten
    // first; after that an admission refuses, and a failure makes room by forgetting the fullest bucket.
    private sealed class BucketTable<TKey>
        where TKey : notnull
    {
        private readonly object m_gate = new();
        private readonly Dictionary<TKey, Bucket> m_buckets = new();
        private readonly List<TKey> m_full = new();
        private readonly IMonotonicClock m_clock;
        private readonly double m_perSecond;
        private readonly double m_capacity;
        private readonly int m_maxKeys;
        private readonly bool m_evictsWhenFull;

        public BucketTable(IMonotonicClock clock, double perSecond, int capacity, int maxKeys, bool evictsWhenFull)
        {
            m_clock = clock;
            m_perSecond = perSecond;
            m_capacity = capacity;
            m_maxKeys = maxKeys;
            m_evictsWhenFull = evictsWhenFull;
        }

        public int Count
        {
            get
            {
                lock (m_gate)
                {
                    return m_buckets.Count;
                }
            }
        }

        public bool HasToken(TKey key)
        {
            lock (m_gate)
            {
                if (!m_buckets.TryGetValue(key, out Bucket? bucket))
                {
                    return true;
                }

                bucket.Refill(m_clock.Elapsed, m_perSecond, m_capacity);
                return bucket.Tokens >= 1d;
            }
        }

        public bool TryTake(TKey key)
        {
            lock (m_gate)
            {
                Bucket? bucket = Find(key);
                if (bucket == null || bucket.Tokens < 1d)
                {
                    return false;
                }

                bucket.Tokens -= 1d;
                return true;
            }
        }

        public void Spend(TKey key)
        {
            lock (m_gate)
            {
                Bucket? bucket = Find(key);
                if (bucket != null)
                {
                    bucket.Tokens = Math.Max(0d, bucket.Tokens - 1d);
                }
            }
        }

        // The key's bucket, refilled, added when missing; null when the table is full and cannot make room.
        private Bucket? Find(TKey key)
        {
            TimeSpan now = m_clock.Elapsed;
            if (m_buckets.TryGetValue(key, out Bucket? bucket))
            {
                bucket.Refill(now, m_perSecond, m_capacity);
                return bucket;
            }

            if (m_buckets.Count >= m_maxKeys && !MakeRoom(now))
            {
                return null;
            }

            bucket = new Bucket(now, m_capacity);
            m_buckets.Add(key, bucket);
            return bucket;
        }

        private bool MakeRoom(TimeSpan now)
        {
            m_full.Clear();
            TKey? fullest = default;
            double most = -1d;
            foreach (KeyValuePair<TKey, Bucket> pair in m_buckets)
            {
                pair.Value.Refill(now, m_perSecond, m_capacity);
                if (pair.Value.Tokens >= m_capacity)
                {
                    m_full.Add(pair.Key);
                }
                else if (pair.Value.Tokens > most)
                {
                    most = pair.Value.Tokens;
                    fullest = pair.Key;
                }
            }

            foreach (TKey full in m_full)
            {
                m_buckets.Remove(full);
            }

            if (m_buckets.Count >= m_maxKeys && m_evictsWhenFull && fullest != null)
            {
                m_buckets.Remove(fullest);
            }

            return m_buckets.Count < m_maxKeys;
        }
    }

    private sealed class Bucket
    {
        private TimeSpan m_refilledAt;

        public Bucket(TimeSpan now, double tokens)
        {
            m_refilledAt = now;
            Tokens = tokens;
        }

        public double Tokens { get; set; }

        public void Refill(TimeSpan now, double perSecond, double capacity)
        {
            if (now > m_refilledAt)
            {
                Tokens = Math.Min(capacity, Tokens + (now - m_refilledAt).TotalSeconds * perSecond);
                m_refilledAt = now;
            }
        }
    }
}
}
