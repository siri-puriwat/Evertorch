namespace Evertorch.Persistence
{
/// <summary>
///     One status effect a character had at its last checkpoint, with its time left and its percentages (Persistence
///     §4, §6). An effect that has ended has no row.
/// </summary>
internal sealed class CharacterStatusEffectRow
{
    public long CharacterId { get; set; }

    public string StatusDefinitionId { get; set; } = string.Empty;

    public int RemainingMs { get; set; }

    public int StrPercent { get; set; }

    public int AgiPercent { get; set; }

    public int VitPercent { get; set; }

    public int IntPercent { get; set; }

    public int DexPercent { get; set; }

    public int LukPercent { get; set; }
}
}
