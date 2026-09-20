using Evertorch.Game;

namespace Evertorch.Rules
{
public interface ICharacterRules
{
    DerivedStats CalculateDerivedStats(CharacterBuild build);
}
}
