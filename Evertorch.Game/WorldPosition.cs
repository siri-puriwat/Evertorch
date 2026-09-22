using System;
using System.Globalization;

namespace Evertorch.Game
{
/// <summary>
///     A point in continuous world space: X/Z form the horizontal plane, Y is height, one unit is about one meter.
/// </summary>
public readonly struct WorldPosition : IEquatable<WorldPosition>
{
    public WorldPosition(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    public static bool operator ==(WorldPosition left, WorldPosition right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(WorldPosition left, WorldPosition right)
    {
        return !left.Equals(right);
    }

    // float.Equals rather than == so NaN equals itself and Equals stays consistent with GetHashCode.
    public bool Equals(WorldPosition other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }

    public override bool Equals(object? obj)
    {
        return obj is WorldPosition other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Z);
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }
}
}
