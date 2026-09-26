using System;

namespace Evertorch.Rules
{
/// <summary>
///     A magic attack's inputs: the caster's magic attack and the target's magic defense (skills research note).
/// </summary>
public readonly struct MagicDamageContext
{
    public MagicDamageContext(
        int magicAttack,
        int hardMagicDefense,
        int softMagicDefense,
        IRandomSource random,
        int ratioPercent = 100)
    {
        if (magicAttack < 0 || hardMagicDefense < 0 || softMagicDefense < 0 || ratioPercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(magicAttack), "Magic damage inputs cannot be negative.");
        }

        MagicAttack = magicAttack;
        HardMagicDefense = hardMagicDefense;
        SoftMagicDefense = softMagicDefense;
        Random = random ?? throw new ArgumentNullException(nameof(random));
        RatioPercent = ratioPercent;
    }

    /// <summary>The value rolled between 80 % and 120 %: a monster's defined magic attack.</summary>
    public int MagicAttack { get; }

    /// <summary>Equipment magic defense, applied as a ratio.</summary>
    public int HardMagicDefense { get; }

    /// <summary>Stat-derived magic defense, subtracted after the ratio.</summary>
    public int SoftMagicDefense { get; }

    public IRandomSource Random { get; }

    /// <summary>A skill's share of the rolled attack, applied before defense.</summary>
    public int RatioPercent { get; }
}
}
