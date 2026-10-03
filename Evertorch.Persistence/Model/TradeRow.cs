using System;

namespace Evertorch.Persistence
{
/// <summary>
///     A committed trade: its two characters, the lower ID first (Persistence §4).
/// </summary>
internal sealed class TradeRow
{
    public Guid Id { get; set; }

    public long FirstCharacterId { get; set; }

    public long SecondCharacterId { get; set; }

    public DateTime CommittedAt { get; set; }
}
}
