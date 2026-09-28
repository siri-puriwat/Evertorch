using System;

namespace Evertorch.Protocol
{
/// <summary>
///     One primary statistic on a <see cref="CharacterSheet" />: its stored value and the stat points raising it by one
///     costs, 0 at the cap (Gameplay Systems §2).
/// </summary>
public readonly struct CharacterSheetStat : IEquatable<CharacterSheetStat>
{
    public CharacterSheetStat(byte value, byte nextCost)
    {
        Value = value;
        NextCost = nextCost;
    }

    public byte Value { get; }

    public byte NextCost { get; }

    public bool Equals(CharacterSheetStat other)
    {
        return Value == other.Value && NextCost == other.NextCost;
    }

    public override bool Equals(object? obj)
    {
        return obj is CharacterSheetStat other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (Value << 8) | NextCost;
    }
}
}
