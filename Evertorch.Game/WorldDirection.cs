using System;
using System.Globalization;

namespace Evertorch.Game
{
/// <summary>
/// A direction on the horizontal X/Z plane. The type stores what it is given; it does not normalize.
/// </summary>
public readonly struct WorldDirection : IEquatable<WorldDirection>
{
    public WorldDirection(float x, float z)
    {
        X = x;
        Z = z;
    }

    public float X { get; }

    public float Z { get; }

    public static bool operator ==(WorldDirection left, WorldDirection right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(WorldDirection left, WorldDirection right)
    {
        return !left.Equals(right);
    }

    // float.Equals rather than == so NaN equals itself and Equals stays consistent with GetHashCode.
    public bool Equals(WorldDirection other)
    {
        return X.Equals(other.X) && Z.Equals(other.Z);
    }

    public override bool Equals(object? obj)
    {
        return obj is WorldDirection other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Z);
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Z);
    }
}
}
