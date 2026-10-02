using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     A boss's most valuable player as its death decided (Gameplay Systems §10): the character, the MVP experience it
///     gained, and the prize its roll kept, or none.
/// </summary>
public sealed class MostValuablePlayer
{
    public MostValuablePlayer(CharacterSession character, long gainedExperience, MvpDrop? prize)
    {
        Character = character;
        GainedExperience = gainedExperience;
        Prize = prize;
    }

    public CharacterSession Character { get; }

    /// <summary>
    ///     What the MVP experience added, which the base level's cap may cut to 0.
    /// </summary>
    public long GainedExperience { get; }

    public MvpDrop? Prize { get; }
}
}
