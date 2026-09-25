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
}
}
