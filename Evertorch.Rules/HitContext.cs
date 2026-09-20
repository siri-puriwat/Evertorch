using System;

namespace Evertorch.Rules
{
public readonly struct HitContext
{
    public HitContext(
        int attackerHit,
        int attackerCritical,
        int defenderFlee,
        int defenderPerfectDodge,
        int defenderLuk,
        IRandomSource random)
    {
        if (attackerHit < 0 || attackerCritical < 0 || defenderFlee < 0 || defenderPerfectDodge < 0 || defenderLuk < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attackerHit), "Hit inputs cannot be negative.");
        }

        AttackerHit = attackerHit;
        AttackerCritical = attackerCritical;
        DefenderFlee = defenderFlee;
        DefenderPerfectDodge = defenderPerfectDodge;
        DefenderLuk = defenderLuk;
        Random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public int AttackerHit { get; }

    /// <summary>Tenths of a percent.</summary>
    public int AttackerCritical { get; }

    public int DefenderFlee { get; }

    /// <summary>Tenths of a percent.</summary>
    public int DefenderPerfectDodge { get; }

    public int DefenderLuk { get; }

    public IRandomSource Random { get; }
}
}
