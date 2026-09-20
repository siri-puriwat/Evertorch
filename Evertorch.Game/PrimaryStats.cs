using System;
using System.Globalization;

namespace Evertorch.Game
{
/// <summary>
/// The six primary character statistics. Values are base inputs; derived statistics are calculated by the rules.
/// </summary>
public readonly struct PrimaryStats : IEquatable<PrimaryStats>
{
    public PrimaryStats(int str, int agi, int vit, int @int, int dex, int luk)
    {
        if (str < 0 || agi < 0 || vit < 0 || @int < 0 || dex < 0 || luk < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(str), "Primary statistics cannot be negative.");
        }

        Str = str;
        Agi = agi;
        Vit = vit;
        Int = @int;
        Dex = dex;
        Luk = luk;
    }

    public int Str { get; }

    public int Agi { get; }

    public int Vit { get; }

    public int Int { get; }

    public int Dex { get; }

    public int Luk { get; }

    public static bool operator ==(PrimaryStats left, PrimaryStats right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PrimaryStats left, PrimaryStats right)
    {
        return !left.Equals(right);
    }

    public bool Equals(PrimaryStats other)
    {
        return Str == other.Str
               && Agi == other.Agi
               && Vit == other.Vit
               && Int == other.Int
               && Dex == other.Dex
               && Luk == other.Luk;
    }

    public override bool Equals(object? obj)
    {
        return obj is PrimaryStats other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Str, Agi, Vit, Int, Dex, Luk);
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "STR {0} AGI {1} VIT {2} INT {3} DEX {4} LUK {5}",
            Str,
            Agi,
            Vit,
            Int,
            Dex,
            Luk);
    }
}
}
