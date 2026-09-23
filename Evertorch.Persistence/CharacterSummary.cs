namespace Evertorch.Persistence
{
/// <summary>
///     What the character selection list shows of one character.
/// </summary>
public sealed class CharacterSummary
{
    public CharacterSummary(long id, string name, string jobDefinitionId, int baseLevel)
    {
        Id = id;
        Name = name;
        JobDefinitionId = jobDefinitionId;
        BaseLevel = baseLevel;
    }

    public long Id { get; }

    public string Name { get; }

    /// <summary>
    ///     As stored: text the current content may no longer define (Persistence §8).
    /// </summary>
    public string JobDefinitionId { get; }

    public int BaseLevel { get; }
}
}
