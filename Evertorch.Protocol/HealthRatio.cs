using System;

namespace Evertorch.Protocol
{
/// <summary>
///     A monster's HP as the permille clients may see (Network Protocol §9). It rounds up, so a monster that is still
///     alive never shows an empty bar.
/// </summary>
public static class HealthRatio
{
    public const ushort Full = 1000;

    public static ushort ToPermille(int current, int maximum)
    {
        if (maximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "The maximum HP must be positive.");
        }

        if (current <= 0)
        {
            return 0;
        }

        if (current >= maximum)
        {
            return Full;
        }

        return (ushort)((current * 1000L + maximum - 1) / maximum);
    }
}
}
