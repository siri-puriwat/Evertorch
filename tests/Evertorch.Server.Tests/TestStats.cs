using Evertorch.Game;
using Evertorch.Rules;

namespace Evertorch.Server.Tests
{
internal static class TestStats
{
    /// <summary>
    ///     The derived statistics of a level 1 adventurer as the repository content defines one.
    /// </summary>
    public static readonly DerivedStats Adventurer = new RenewalCharacterRules().CalculateDerivedStats(
        new CharacterBuild(1, new PrimaryStats(5, 5, 5, 5, 5, 5), 60, 8, 20, 3, 44, 5f));
}
}
