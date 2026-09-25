using Evertorch.Game;

namespace Evertorch.Rules
{
public interface ICharacterRules
{
    DerivedStats CalculateDerivedStats(CharacterBuild build);

    /// <summary>
    ///     The natural regeneration of a character with <paramref name="stats" /> and the maximums in
    ///     <paramref name="derived" />.
    /// </summary>
    Regeneration CalculateRegeneration(PrimaryStats stats, DerivedStats derived);

    /// <summary>
    ///     The primary statistics with the status effects' <paramref name="percent" /> added to each, floored.
    /// </summary>
    PrimaryStats ApplyStatPercent(PrimaryStats stats, StatPercentages percent);
}
}
