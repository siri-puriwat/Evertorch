using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     How a character creation ended, with the account's characters afterwards.
/// </summary>
public sealed class CharacterCreation
{
    public CharacterCreation(CharacterCreationStatus status, long characterId,
        IReadOnlyList<CharacterSummary> characters)
    {
        Status = status;
        CharacterId = characterId;
        Characters = characters;
    }

    public CharacterCreationStatus Status { get; }

    /// <summary>
    ///     The new character's ID when <see cref="Status" /> is <see cref="CharacterCreationStatus.Created" />; else 0.
    /// </summary>
    public long CharacterId { get; }

    public IReadOnlyList<CharacterSummary> Characters { get; }
}
}
