using System;
using System.Globalization;

namespace Evertorch.Server
{
/// <summary>
///     Identifies one transport connection for as long as the process runs. Values are never reused, so a late event
///     for a closed connection cannot be mistaken for a new one.
/// </summary>
public readonly struct ConnectionId : IEquatable<ConnectionId>
{
    public ConnectionId(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public static bool operator ==(ConnectionId left, ConnectionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ConnectionId left, ConnectionId right)
    {
        return !left.Equals(right);
    }

    public bool Equals(ConnectionId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is ConnectionId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}
}
