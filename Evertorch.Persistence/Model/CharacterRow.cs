using System;

namespace Evertorch.Persistence
{
internal sealed class CharacterRow
{
    public long Id { get; set; }

    public long AccountId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NameNormalized { get; set; } = string.Empty;

    public string JobDefinitionId { get; set; } = string.Empty;

    public int BaseLevel { get; set; }

    public int JobLevel { get; set; }

    public long BaseExp { get; set; }

    public long JobExp { get; set; }

    public int Str { get; set; }

    public int Agi { get; set; }

    public int Vit { get; set; }

    public int Int { get; set; }

    public int Dex { get; set; }

    public int Luk { get; set; }

    public int Hp { get; set; }

    public int Sp { get; set; }

    public long Currency { get; set; }

    public string MapDefinitionId { get; set; } = string.Empty;

    public float PositionX { get; set; }

    public float PositionY { get; set; }

    public float PositionZ { get; set; }

    /// <summary>
    ///     The inventory revision: an unsigned 32-bit number kept in a wider column, incremented by every committed
    ///     inventory change.
    /// </summary>
    public long InventoryRevision { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastPlayedAt { get; set; }

    public int Version { get; set; }
}
}
