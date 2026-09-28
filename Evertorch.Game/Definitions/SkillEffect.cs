using System;

namespace Evertorch.Game
{
/// <summary>
///     The one thing a skill does when it resolves (Gameplay Systems §9): damage as a percentage of the caster's basic
///     attack, a flat heal, or a status effect for a time at a strength.
/// </summary>
public sealed class SkillEffect
{
    private SkillEffect(
        SkillEffectKind kind,
        int damageRatioPercent,
        int healHp,
        StatusDefinitionId status,
        int statusDurationMs,
        StatPercentages statPercent)
    {
        Kind = kind;
        DamageRatioPercent = damageRatioPercent;
        HealHp = healHp;
        Status = status;
        StatusDurationMs = statusDurationMs;
        StatPercent = statPercent;
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

    /// <summary>
    ///     For a status effect, the effect it starts or renews.
    /// </summary>
    public StatusDefinitionId Status { get; }

    public int StatusDurationMs { get; }

    /// <summary>
    ///     For a status effect, what it adds to each primary statistic while it lasts, as a percentage of the statistic
    ///     (Gameplay Systems §9.1).
    /// </summary>
    public StatPercentages StatPercent { get; }

    public static SkillEffect Damage(int ratioPercent)
    {
        if (ratioPercent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratioPercent), "A damage ratio must be positive.");
        }

        return new SkillEffect(SkillEffectKind.Damage, ratioPercent, 0, default, 0, default);
    }

    public static SkillEffect Heal(int hp)
    {
        if (hp <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hp), "A heal must restore something.");
        }

        return new SkillEffect(SkillEffectKind.Heal, 0, hp, default, 0, default);
    }

    public static SkillEffect StatusEffect(StatusDefinitionId status, int durationMs, StatPercentages statPercent)
    {
        if (status == default)
        {
            throw new ArgumentException("A status effect names its status.", nameof(status));
        }

        if (durationMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "A status effect must last.");
        }

        return new SkillEffect(SkillEffectKind.Status, 0, 0, status, durationMs, statPercent);
    }
}
}
