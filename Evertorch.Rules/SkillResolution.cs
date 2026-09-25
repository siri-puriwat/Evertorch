namespace Evertorch.Rules
{
/// <summary>
///     What a skill did: for damage, the hit roll and the damage; for a heal, the nominal amount, whatever HP the
///     target was missing.
/// </summary>
public readonly struct SkillResolution
{
    public SkillResolution(SkillResult result, int amount)
    {
        Result = result;
        Amount = amount;
    }

    public SkillResult Result { get; }

    /// <summary>
    ///     Damage dealt, 0 unless it hit; for a heal, the nominal amount.
    /// </summary>
    public int Amount { get; }
}
}
