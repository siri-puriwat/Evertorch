using System;

namespace Evertorch.Game
{
/// <summary>
///     A percentage added to each primary statistic, as a status effect grants it (Gameplay Systems §9.1). The rules
///     apply it to the character's statistics before equipment bonuses.
/// </summary>
public readonly struct StatPercentages : IEquatable<StatPercentages>
{
    public StatPercentages(int str, int agi, int vit, int @int, int dex, int luk)
    {
        if (str < 0 || agi < 0 || vit < 0 || @int < 0 || dex < 0 || luk < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(str), "A status effect's percentages cannot be negative.");
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

    public static bool operator ==(StatPercentages left, StatPercentages right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StatPercentages left, StatPercentages right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     The percentages of two effects active together, which add up.
    /// </summary>
    public StatPercentages Plus(StatPercentages other)
    {
        return new StatPercentages(
            Str + other.Str,
            Agi + other.Agi,
            Vit + other.Vit,
            Int + other.Int,
            Dex + other.Dex,
            Luk + other.Luk);
    }

    public bool Equals(StatPercentages other)
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
        return obj is StatPercentages other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Str, Agi, Vit, Int, Dex, Luk);
    }
}
}
