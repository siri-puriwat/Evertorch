using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What one committed inventory change did, for the chat's system lines (Prototype Content §2): the coins before and
///     after
///     it, and how many units of each item it added, negative for the units it took. An item it left as it was is absent,
///     so a change that only put a row on or took it off adds nothing.
/// </summary>
public sealed class InventoryDelta
{
    public InventoryDelta(
        uint coinsBefore,
        uint coinsAfter,
        IReadOnlyDictionary<ItemDefinitionId, long> units,
        byte refineLevel = 0,
        IReadOnlyList<ItemDefinitionId>? cards = null,
        ItemDefinitionId insertedCard = default,
        InventoryEntry insertedInto = default)
    {
        CoinsBefore = coinsBefore;
        CoinsAfter = coinsAfter;
        Units = units ?? throw new ArgumentNullException(nameof(units));
        RefineLevel = refineLevel;
        Cards = cards ?? Array.Empty<ItemDefinitionId>();
        InsertedCard = insertedCard;
        InsertedInto = insertedInto;
    }

    public uint CoinsBefore { get; }

    public uint CoinsAfter { get; }

    public IReadOnlyDictionary<ItemDefinitionId, long> Units { get; }

    /// <summary>
    ///     The refine level of the rows whose units moved, the highest of them: a sold row's, which its words name.
    /// </summary>
    public byte RefineLevel { get; }

    /// <summary>
    ///     The cards of the row whose units moved, as the client held it: a sold row's, which its words name.
    /// </summary>
    public IReadOnlyList<ItemDefinitionId> Cards { get; }

    /// <summary>
    ///     The card that joined a row's cards, which an insertion's words name; default when no row's cards grew.
    /// </summary>
    public ItemDefinitionId InsertedCard { get; }

    /// <summary>
    ///     The row whose cards grew, as the change left it.
    /// </summary>
    public InventoryEntry InsertedInto { get; }
}
}
