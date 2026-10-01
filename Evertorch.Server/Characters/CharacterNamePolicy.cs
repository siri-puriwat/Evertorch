using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     The naming policy for a new character: the shared rule of <see cref="CharacterNames" />.
/// </summary>
public static class CharacterNamePolicy
{
    public static bool IsValid(string name)
    {
        return CharacterNames.IsValid(name);
    }
}
}
