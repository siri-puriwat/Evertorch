using System;

namespace Evertorch.Game
{
/// <summary>
///     The one thing a skill does when it resolves (Gameplay Systems §9): damage as a percentage of the caster's basic
///     attack, or a flat heal.
/// </summary>
public sealed class SkillEffect
{
    private SkillEffect(SkillEffectKind kind, int damageRatioPercent, int healHp)
    {
        Kind = kind;
        DamageRatioPercent = damageRatioPercent;
        HealHp = healHp;
    }

    public SkillEffectKind Kind { get; }

    /// <summary>
    ///     For damage, the percentage of the basic attack's raw damage, applied before defense.
    /// </summary>
    public int DamageRatioPercent { get; }

    /// <summary>
    ///     For a heal, the HP restored, capped at the maximum.
    /// </summary>
    public int HealHp { get; }

    public static SkillEffect Damage(int ratioPercent)
    {
        if (ratioPercent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratioPercent), "A damage ratio must be positive.");
        }

        return new SkillEffect(SkillEffectKind.Damage, ratioPercent, 0);
    }

    public static SkillEffect Heal(int hp)
    {
        if (hp <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hp), "A heal must restore something.");
        }

        return new SkillEffect(SkillEffectKind.Heal, 0, hp);
    }
}
}
