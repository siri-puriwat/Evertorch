namespace Evertorch.Game
{
public sealed class SkillDefinition
{
    public SkillDefinition(
        SkillDefinitionId id,
        string displayName,
        SkillTargetType targetType,
        SkillDamageType? damageType,
        double range,
        int spCost,
        SkillPaymentPoint spPaidAt,
        int fixedCastMs,
        int variableCastMs,
        int afterCastDelayMs,
        int cooldownMs,
        SkillEffect? effect)
    {
        Id = id;
        DisplayName = displayName;
        TargetType = targetType;
        DamageType = damageType;
        Range = range;
        SpCost = spCost;
        SpPaidAt = spPaidAt;
        FixedCastMs = fixedCastMs;
        VariableCastMs = variableCastMs;
        AfterCastDelayMs = afterCastDelayMs;
        CooldownMs = cooldownMs;
        Effect = effect;
    }

    public SkillDefinitionId Id { get; }

    public string DisplayName { get; }

    public SkillTargetType TargetType { get; }

    /// <summary>
    ///     Required for a job's basic attack and a damage effect; absent otherwise.
    /// </summary>
    public SkillDamageType? DamageType { get; }

    /// <summary>World units.</summary>
    public double Range { get; }

    public int SpCost { get; }

    public SkillPaymentPoint SpPaidAt { get; }

    public int FixedCastMs { get; }

    /// <summary>
    ///     The part of the cast time a character's statistics shorten.
    /// </summary>
    public int VariableCastMs { get; }

    public int AfterCastDelayMs { get; }

    public int CooldownMs { get; }

    /// <summary>
    ///     What the skill does when it resolves; a job's basic attack has none, since swings do not go through skills.
    /// </summary>
    public SkillEffect? Effect { get; }
}
}
