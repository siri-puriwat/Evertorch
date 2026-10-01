using System;

namespace Evertorch.Client
{
/// <summary>
///     The client's own chat bucket (Network Protocol §11): a throttled <c>ChatSend</c> scores a violation on the
///     server, so the client refuses a line itself before the server would. It is one line stricter than the server's
///     burst of 5 at 1 a second, which absorbs lines that arrive bunched by up to about a second of network delay. The
///     party's commands keep a bucket of the same size, stricter still than the server's 5 at 2 a second.
/// </summary>
public sealed class ChatThrottle
{
    public const int Burst = 4;
    public const double PerSecond = 1d;

    private double m_tokens = Burst;
    private double m_refilledAt = double.NaN;

    /// <summary>
    ///     Spends a line at <paramref name="nowSeconds" />, a monotonic clock; false when none is left.
    /// </summary>
    public bool TryTake(double nowSeconds)
    {
        if (!double.IsNaN(m_refilledAt) && nowSeconds > m_refilledAt)
        {
            m_tokens = Math.Min(Burst, m_tokens + (nowSeconds - m_refilledAt) * PerSecond);
        }

        m_refilledAt = double.IsNaN(m_refilledAt) ? nowSeconds : Math.Max(m_refilledAt, nowSeconds);
        if (m_tokens < 1d)
        {
            return false;
        }

        m_tokens -= 1d;
        return true;
    }
}
}
