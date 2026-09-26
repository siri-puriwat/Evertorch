using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     What one committed inventory change did, for the feedback lines (Prototype Content §2): the coins before and after
///     it, and how many units of each item it added, negative for the units it took. An item it left as it was is absent,
///     so a change that only put a row on or took it off adds nothing.
/// </summary>
public sealed class InventoryDelta
{
    public InventoryDelta(uint coinsBefore, uint coinsAfter, IReadOnlyDictionary<ItemDefinitionId, long> units)
    {
        CoinsBefore = coinsBefore;
        CoinsAfter = coinsAfter;
        Units = units ?? throw new ArgumentNullException(nameof(units));
    }

    public uint CoinsBefore { get; }

    public uint CoinsAfter { get; }

    public IReadOnlyDictionary<ItemDefinitionId, long> Units { get; }
}
}
