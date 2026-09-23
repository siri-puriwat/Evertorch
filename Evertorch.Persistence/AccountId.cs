using System;

namespace Evertorch.Persistence
{
/// <summary>
///     Identifies one account. It is server-side only: accounts never cross the gameplay protocol.
/// </summary>
public readonly struct AccountId : IEquatable<AccountId>
{
    public AccountId(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public static bool operator ==(AccountId left, AccountId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(AccountId left, AccountId right)
    {
        return !left.Equals(right);
    }

    public bool Equals(AccountId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is AccountId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
}
