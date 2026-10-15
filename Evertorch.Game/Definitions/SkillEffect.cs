using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
///     The one thing a skill does when it resolves (Gameplay Systems §9): damage as a percentage of the caster's basic
///     attack, with the ailment it may bring; a flat heal, with the ailments it ends; or a status effect for a time at a
///     strength, with its chance when the status is an ailment.
/// </summary>
public sealed class SkillEffect
{
    private static readonly IReadOnlyList<Ailment> NoCures = Array.Empty<Ailment>();

    private SkillEffect(
        SkillEffectKind kind,
        int damageRatioPercent,
        int healHp,
        StatusDefinitionId status,
        int statusDurationMs,
        StatPercentages statPercent,
        AilmentRider? rider,
        int chanceBasisPoints,
        IReadOnlyList<Ailment> cures)
    {
        Kind = kind;
        DamageRatioPercent = damageRatioPercent;
        HealHp = healHp;
        Status = status;
        StatusDurationMs = statusDurationMs;
        StatPercent = statPercent;
        Rider = rider;
        ChanceBasisPoints = chanceBasisPoints;
        Cures = cures;
    }

    public SkillEffectKind Kind { get; }

    /// <summary>
    ///     For damage, the percentage of the basic attack's raw damage, applied before defense.
    /// </summary>
    public int DamageRatioPercent { get; }

    /// <summary>
    ///     For damage, the ailment a landed hit may also bring; null for none. Server-only content.
    /// </summary>
    public AilmentRider? Rider { get; }

    /// <summary>
    ///     For a heal, the HP restored, capped at the maximum.
    /// </summary>
    public int HealHp { get; }

    /// <summary>
    ///     For a heal, the ailments it ends (Gameplay Systems §11.2); empty for none. Server-only content.
    /// </summary>
    public IReadOnlyList<Ailment> Cures { get; }

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

    /// <summary>
    ///     For a status effect that is an ailment, its chance to land on each target before resistance, in basis points;
    ///     10,000, certain, for every other effect. Server-only content.
    /// </summary>
    public int ChanceBasisPoints { get; }

    public static SkillEffect Damage(int ratioPercent, AilmentRider? rider = null)
    {
        if (ratioPercent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratioPercent), "A damage ratio must be positive.");
        }

        return new SkillEffect(SkillEffectKind.Damage, ratioPercent, 0, default, 0, default, rider,
            ContentLimits.MaxChanceBasisPoints, NoCures);
    }

    public static SkillEffect Heal(int hp, IReadOnlyList<Ailment>? cures = null)
    {
        if (hp <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hp), "A heal must restore something.");
        }

        return new SkillEffect(SkillEffectKind.Heal, 0, hp, default, 0, default, null,
            ContentLimits.MaxChanceBasisPoints, Checked(cures));
    }

    public static SkillEffect StatusEffect(
        StatusDefinitionId status,
        int durationMs,
        StatPercentages statPercent,
        int chanceBasisPoints = ContentLimits.MaxChanceBasisPoints)
    {
        if (status == default)
        {
            throw new ArgumentException("A status effect names its status.", nameof(status));
        }

        if (durationMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "A status effect must last.");
        }

        if (chanceBasisPoints < 1 || chanceBasisPoints > ContentLimits.MaxChanceBasisPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(chanceBasisPoints), chanceBasisPoints,
                "A chance is 1 to 10,000 basis points.");
        }

        return new SkillEffect(SkillEffectKind.Status, 0, 0, status, durationMs, statPercent, null, chanceBasisPoints,
            NoCures);
    }

    /// <summary>
    ///     The ailments a heal or a consumable ends: each one of the six, at most once.
    /// </summary>
    internal static IReadOnlyList<Ailment> Checked(IReadOnlyList<Ailment>? cures)
    {
        if (cures == null || cures.Count == 0)
        {
            return NoCures;
        }

        var seen = new HashSet<Ailment>();
        foreach (Ailment ailment in cures)
        {
            if (ailment == Ailment.None || ailment > Ailment.Blind || !seen.Add(ailment))
            {
                throw new ArgumentException("A cure names each of the six ailments at most once.", nameof(cures));
            }
        }

        return cures;
    }
}
}
