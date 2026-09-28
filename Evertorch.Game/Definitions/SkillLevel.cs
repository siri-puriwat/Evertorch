namespace Evertorch.Game
{
/// <summary>
///     What one level of a skill does and costs (Gameplay Systems §9): its SP cost, its fixed and variable cast time,
///     its after-cast delay, its cooldown, and its effect.
/// </summary>
public sealed class SkillLevel
{
    public SkillLevel(
        int spCost,
        int fixedCastMs,
        int variableCastMs,
        int afterCastDelayMs,
        int cooldownMs,
        SkillEffect effect)
    {
        SpCost = spCost;
        FixedCastMs = fixedCastMs;
        VariableCastMs = variableCastMs;
        AfterCastDelayMs = afterCastDelayMs;
        CooldownMs = cooldownMs;
        Effect = effect;
    }

    public int SpCost { get; }

    public int FixedCastMs { get; }

    /// <summary>
    ///     The part of the cast time a character's statistics shorten.
    /// </summary>
    public int VariableCastMs { get; }

    public int AfterCastDelayMs { get; }

    public int CooldownMs { get; }

    public SkillEffect Effect { get; }
}
}
