using System;

namespace Evertorch.Rules
{
/// <summary>
///     How long a skill takes to cast, and what follows its resolution: the after-cast delay, during which the caster
///     can neither swing nor cast, and the skill's own cooldown. All in whole milliseconds.
/// </summary>
public readonly struct CastTiming
{
    public CastTiming(int castMs, int afterCastDelayMs, int cooldownMs)
    {
        if (castMs < 0 || afterCastDelayMs < 0 || cooldownMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(castMs), "Cast timing cannot be negative.");
        }

        CastMs = castMs;
        AfterCastDelayMs = afterCastDelayMs;
        CooldownMs = cooldownMs;
    }

    public int CastMs { get; }

    public int AfterCastDelayMs { get; }

    public int CooldownMs { get; }
}
}
