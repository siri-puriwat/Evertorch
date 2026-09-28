namespace Evertorch.Persistence
{
/// <summary>
///     One skill a character has learned, at its level (Persistence §4). A skill not learned has no row.
/// </summary>
internal sealed class CharacterSkillRow
{
    public long CharacterId { get; set; }

    public string SkillDefinitionId { get; set; } = string.Empty;

    public int Level { get; set; }
}
}
