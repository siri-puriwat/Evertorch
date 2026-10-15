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
        IRandomSource random,
        bool isDefenderHeld = false,
        bool isDefenderAsleep = false,
        bool isAttackerBlinded = false,
        bool isDefenderBlinded = false)
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
        IsDefenderHeld = isDefenderHeld;
        IsDefenderAsleep = isDefenderAsleep;
        IsAttackerBlinded = isAttackerBlinded;
        IsDefenderBlinded = isDefenderBlinded;
    }

    public int AttackerHit { get; }

    /// <summary>Tenths of a percent.</summary>
    public int AttackerCritical { get; }

    public int DefenderFlee { get; }

    /// <summary>Tenths of a percent.</summary>
    public int DefenderPerfectDodge { get; }

    public int DefenderLuk { get; }

    public IRandomSource Random { get; }

    /// <summary>
    ///     Whether the defender is stunned, frozen, or asleep (Gameplay Systems §9.1): then every hit lands but a perfect
    ///     dodge.
    /// </summary>
    public bool IsDefenderHeld { get; }

    /// <summary>Whether the defender sleeps, which doubles the attacker's critical chance.</summary>
    public bool IsDefenderAsleep { get; }

    /// <summary>Whether the attacker is blinded, which takes a quarter off its HIT.</summary>
    public bool IsAttackerBlinded { get; }

    /// <summary>Whether the defender is blinded, which takes a quarter off its FLEE.</summary>
    public bool IsDefenderBlinded { get; }

    /// <summary>
    ///     The same inputs with no chance of a critical, as a skill rolls (Gameplay Systems §9); the critical stage
    ///     still draws.
    /// </summary>
    public HitContext WithoutCritical()
    {
        return new HitContext(
            AttackerHit,
            0,
            DefenderFlee,
            DefenderPerfectDodge,
            DefenderLuk,
            Random,
            IsDefenderHeld,
            IsDefenderAsleep,
            IsAttackerBlinded,
            IsDefenderBlinded);
    }
}
}
