using System;
using System.Globalization;

namespace Evertorch.Game
{
/// <summary>
/// Identifies one live entity in the authoritative world. It is never interchangeable with a definition ID.
/// </summary>
public readonly struct EntityId : IEquatable<EntityId>
{
    public EntityId(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public static bool operator ==(EntityId left, EntityId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EntityId left, EntityId right)
    {
        return !left.Equals(right);
    }

    public bool Equals(EntityId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is EntityId other && Equals(other);
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
