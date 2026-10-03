using System;
using System.Collections.Generic;
using System.Linq;

namespace Evertorch.Persistence
{
/// <summary>
///     What one trader gives: whole or part rows of its bag, by row, and coins.
/// </summary>
public sealed class TraderOffer
{
    public TraderOffer(long characterId, IReadOnlyList<TradeLine> lines, long coins)
    {
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        if (coins < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coins), "Coins are not negative.");
        }

        if (lines.Select(line => line.InventoryItemId).Distinct().Count() != lines.Count)
        {
            throw new ArgumentException("Each row is offered once.", nameof(lines));
        }

        CharacterId = characterId;
        Coins = coins;
    }

    public long CharacterId { get; }

    public IReadOnlyList<TradeLine> Lines { get; }

    public long Coins { get; }
}
}
