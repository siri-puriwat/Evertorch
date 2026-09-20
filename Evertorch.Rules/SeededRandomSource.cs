using System;

namespace Evertorch.Rules
{
/// <summary>
/// SplitMix64 generator. It is implemented here rather than taken from the runtime so a seed yields the same
/// sequence on every platform and framework version.
/// </summary>
public sealed class SeededRandomSource : IRandomSource
{
    private ulong m_state;

    public SeededRandomSource(ulong seed)
    {
        m_state = seed;
    }

    public int Next(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax), "The bound must be positive.");
        }

        // Multiply-shift maps 32 random bits onto the range without the low-bit bias of a modulo.
        ulong high = NextUInt64() >> 32;
        return (int)((high * (ulong)exclusiveMax) >> 32);
    }

    private ulong NextUInt64()
    {
        unchecked
        {
            m_state += 0x9E3779B97F4A7C15UL;
            ulong value = m_state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
}
