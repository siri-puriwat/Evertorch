using System;
using System.Globalization;

namespace Evertorch.Game
{
/// <summary>
/// The single movement intent every control scheme produces. The direction is a request: the server normalizes it
/// and never treats its magnitude as a speed multiplier.
/// </summary>
public readonly struct MoveIntent : IEquatable<MoveIntent>
{
    public MoveIntent(uint sequence, uint clientTick, float directionX, float directionZ)
    {
        Sequence = sequence;
        ClientTick = clientTick;
        DirectionX = directionX;
        DirectionZ = directionZ;
    }

    public uint Sequence { get; }

    public uint ClientTick { get; }

    public float DirectionX { get; }

    public float DirectionZ { get; }

    public static bool operator ==(MoveIntent left, MoveIntent right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MoveIntent left, MoveIntent right)
    {
        return !left.Equals(right);
    }

    // float.Equals rather than == so NaN equals itself and Equals stays consistent with GetHashCode.
    public bool Equals(MoveIntent other)
    {
        return Sequence == other.Sequence
               && ClientTick == other.ClientTick
               && DirectionX.Equals(other.DirectionX)
               && DirectionZ.Equals(other.DirectionZ);
    }

    public override bool Equals(object? obj)
    {
        return obj is MoveIntent other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Sequence, ClientTick, DirectionX, DirectionZ);
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "#{0} tick {1} ({2}, {3})",
            Sequence,
            ClientTick,
            DirectionX,
            DirectionZ);
    }
}
}
