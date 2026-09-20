using System;
using System.Globalization;

namespace Evertorch.Game
{
/// <summary>
/// Identifies one durable character. It outlives any live <see cref="EntityId" /> the character is given in a map.
/// </summary>
public readonly struct CharacterId : IEquatable<CharacterId>
{
    public CharacterId(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public static bool operator ==(CharacterId left, CharacterId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CharacterId left, CharacterId right)
    {
        return !left.Equals(right);
    }

    public bool Equals(CharacterId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is CharacterId other && Equals(other);
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
