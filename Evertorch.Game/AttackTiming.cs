using System;
using System.Globalization;

namespace Evertorch.Game
{
/// <summary>
/// Authoritative timing of one basic attack. <see cref="Impact" /> is measured from the start of the attack;
/// <see cref="Interval" /> is the earliest time the next attack may begin.
/// </summary>
public readonly struct AttackTiming : IEquatable<AttackTiming>
{
    public AttackTiming(TimeSpan interval, TimeSpan windup, TimeSpan impact, TimeSpan recovery)
    {
        if (interval < TimeSpan.Zero || windup < TimeSpan.Zero || impact < TimeSpan.Zero || recovery < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Attack timing phases cannot be negative.");
        }

        if (impact > interval)
        {
            throw new ArgumentException("Impact cannot fall after the attack interval.", nameof(impact));
        }

        Interval = interval;
        Windup = windup;
        Impact = impact;
        Recovery = recovery;
    }

    public TimeSpan Interval { get; }

    public TimeSpan Windup { get; }

    public TimeSpan Impact { get; }

    public TimeSpan Recovery { get; }

    public static bool operator ==(AttackTiming left, AttackTiming right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(AttackTiming left, AttackTiming right)
    {
        return !left.Equals(right);
    }

    public bool Equals(AttackTiming other)
    {
        return Interval == other.Interval
            && Windup == other.Windup
            && Impact == other.Impact
            && Recovery == other.Recovery;
    }

    public override bool Equals(object? obj)
    {
        return obj is AttackTiming other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Interval, Windup, Impact, Recovery);
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "interval {0} ms, windup {1} ms, impact {2} ms, recovery {3} ms",
            Interval.TotalMilliseconds,
            Windup.TotalMilliseconds,
            Impact.TotalMilliseconds,
            Recovery.TotalMilliseconds);
    }
}
}
