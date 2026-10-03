using System;
using System.Collections.Generic;
using System.Linq;

namespace Evertorch.Persistence
{
/// <summary>
///     What a trade does to its two traders' bags and coins (Persistence §5), worked out before anything is written:
///     every offer checked against what its giver holds, then each receiver's room with the giver's own lines taken out
///     first, then the coins against their cap; and, when it all holds, where each line ends up. A whole row changes
///     owner unless its receiver holds a stack of the item to merge into; part of a row merges into that stack or
///     becomes a row of its own. The PostgreSQL store writes the outcome, and the in-memory store keeps it.
/// </summary>
public sealed class TradeSettlement
{
    private readonly TradeCommit m_trade;
    private readonly List<Holding> m_holdings;
    private readonly List<Movement> m_movements = new();

    /// <param name="trade">The trade to settle.</param>
    /// <param name="holdings">Every row of both traders' bags, in row order.</param>
    /// <param name="firstCoins">The first trader's coins.</param>
    /// <param name="secondCoins">The second trader's coins.</param>
    public TradeSettlement(TradeCommit trade, IEnumerable<Holding> holdings, long firstCoins, long secondCoins)
    {
        m_trade = trade ?? throw new ArgumentNullException(nameof(trade));
        m_holdings = holdings?.ToList() ?? throw new ArgumentNullException(nameof(holdings));
        FirstCoins = firstCoins - trade.First.Coins + trade.Second.Coins;
        SecondCoins = secondCoins - trade.Second.Coins + trade.First.Coins;
        Status = Check(firstCoins, secondCoins);
        if (Status == TradeStatus.Committed)
        {
            Move();
        }
    }

    public TradeStatus Status { get; }

    /// <summary>
    ///     The side a refusal names, or null.
    /// </summary>
    public long? RefusedCharacterId { get; private set; }

    /// <summary>
    ///     Every row of both bags as the trade leaves it: a row the trade emptied at 0, and the rows it made, with no ID
    ///     yet, after them.
    /// </summary>
    public IReadOnlyList<Holding> Holdings => m_holdings;

    /// <summary>
    ///     Each line moved, its coins included, in the order of the ledger's lines.
    /// </summary>
    public IReadOnlyList<Movement> Movements => m_movements;

    public long FirstCoins { get; }

    public long SecondCoins { get; }

    private IEnumerable<(TraderOffer Giver, long Receiver)> Sides()
    {
        yield return (m_trade.First, m_trade.Second.CharacterId);
        yield return (m_trade.Second, m_trade.First.CharacterId);
    }

    private TradeStatus Check(long firstCoins, long secondCoins)
    {
        foreach ((TraderOffer giver, long _) in Sides())
        {
            long held = giver.CharacterId == m_trade.First.CharacterId ? firstCoins : secondCoins;
            bool isHeld = giver.Coins <= held && giver.Lines.All(line =>
            {
                Holding? row = Find(line.InventoryItemId, giver.CharacterId);
                return row != null && !row.IsWorn && row.Quantity >= line.Quantity;
            });
            if (!isHeld)
            {
                return Refuse(TradeStatus.Refused, giver.CharacterId);
            }
        }

        foreach ((TraderOffer giver, long receiver) in Sides())
        {
            if (!HasRoom(giver, receiver))
            {
                return Refuse(TradeStatus.InventoryFull, receiver);
            }
        }

        if (FirstCoins > EvertorchDbContext.MaxCurrency)
        {
            return Refuse(TradeStatus.CoinCapReached, m_trade.First.CharacterId);
        }

        return SecondCoins > EvertorchDbContext.MaxCurrency
            ? Refuse(TradeStatus.CoinCapReached, m_trade.Second.CharacterId)
            : TradeStatus.Committed;
    }

    private TradeStatus Refuse(TradeStatus status, long characterId)
    {
        RefusedCharacterId = characterId;
        return status;
    }

    // The receiver's bag with its own offer taken out first, then every line it is given: a stackable item merges into
    // a stack it keeps, or the first such line makes one; anything else takes a row.
    private bool HasRoom(TraderOffer giver, long receiver)
    {
        TraderOffer own = giver == m_trade.First ? m_trade.Second : m_trade.First;
        var kept = new List<(string Item, int Quantity)>();
        foreach (Holding row in m_holdings.Where(row => row.CharacterId == receiver))
        {
            int given = own.Lines.Where(line => line.InventoryItemId == row.Id).Sum(line => line.Quantity);
            if (row.Quantity > given)
            {
                kept.Add((row.ItemDefinitionId, row.Quantity - given));
            }
        }

        int rows = kept.Count;
        var stacks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TradeLine line in giver.Lines)
        {
            string item = Find(line.InventoryItemId, giver.CharacterId)!.ItemDefinitionId;
            int limit = StackLimit(item);
            if (limit > 1 && !stacks.ContainsKey(item))
            {
                int index = kept.FindIndex(row => row.Item == item);
                if (index >= 0)
                {
                    stacks.Add(item, kept[index].Quantity);
                }
            }

            if (limit > 1 && stacks.TryGetValue(item, out int held))
            {
                stacks[item] = held + line.Quantity;
            }
            else
            {
                rows++;
                if (limit > 1)
                {
                    stacks.Add(item, line.Quantity);
                }
            }

            if (line.Quantity > limit || (limit > 1 && stacks[item] > limit))
            {
                return false;
            }
        }

        return rows <= m_trade.MaxRows;
    }

    // Every line leaves its giver first, so a receiver's own offer is out of its bag before anything arrives.
    private void Move()
    {
        var whole = new HashSet<Holding>();
        foreach ((TraderOffer giver, long _) in Sides())
        {
            foreach (TradeLine line in giver.Lines)
            {
                Holding row = Find(line.InventoryItemId, giver.CharacterId)!;
                if (row.Quantity == line.Quantity)
                {
                    whole.Add(row);
                }
                else
                {
                    row.Quantity -= line.Quantity;
                }
            }
        }

        int number = 0;
        foreach ((TraderOffer giver, long receiver) in Sides())
        {
            foreach (TradeLine line in giver.Lines)
            {
                Holding row = Find(line.InventoryItemId, giver.CharacterId)!;
                bool isWhole = whole.Remove(row);
                Holding? stack = StackLimit(row.ItemDefinitionId) > 1
                    ? m_holdings.FirstOrDefault(held => held.CharacterId == receiver
                        && held.ItemDefinitionId == row.ItemDefinitionId
                        && held.Quantity > 0
                        && !whole.Contains(held))
                    : null;
                Holding received;
                if (stack != null)
                {
                    stack.Quantity += line.Quantity;
                    if (isWhole)
                    {
                        row.Quantity = 0;
                    }

                    received = stack;
                }
                else if (isWhole)
                {
                    row.CharacterId = receiver;
                    received = row;
                }
                else
                {
                    received = new Holding(null, receiver, row.ItemDefinitionId, line.Quantity, false,
                        line.InventoryItemId);
                    m_holdings.Add(received);
                }

                m_movements.Add(new Movement(
                    ++number,
                    giver.CharacterId,
                    receiver,
                    line.InventoryItemId,
                    row.ItemDefinitionId,
                    line.Quantity,
                    0,
                    received));
            }

            if (giver.Coins > 0)
            {
                m_movements.Add(new Movement(++number, giver.CharacterId, receiver, null, null, 0, giver.Coins, null));
            }
        }
    }

    private Holding? Find(long id, long characterId)
    {
        return m_holdings.FirstOrDefault(row => row.Id == id && row.CharacterId == characterId);
    }

    private int StackLimit(string item)
    {
        return m_trade.StackLimits.TryGetValue(item, out int limit)
            ? limit
            : throw new InvalidOperationException($"No stack limit was given for {item}.");
    }

    /// <summary>
    ///     A row of a trader's bag: an existing row, or one the trade makes from part of <see cref="SourceId" />.
    /// </summary>
    public sealed class Holding
    {
        public Holding(
            long? id,
            long characterId,
            string itemDefinitionId,
            int quantity,
            bool isWorn,
            long sourceId = 0)
        {
            Id = id;
            CharacterId = characterId;
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
            IsWorn = isWorn;
            SourceId = sourceId;
        }

        /// <summary>
        ///     The row's ID; null for a row the trade makes.
        /// </summary>
        public long? Id { get; }

        public long CharacterId { get; set; }

        public string ItemDefinitionId { get; }

        public int Quantity { get; set; }

        public bool IsWorn { get; }

        /// <summary>
        ///     For a row the trade makes, the row whose part it holds, whose refine level and instance data it keeps.
        /// </summary>
        public long SourceId { get; }
    }

    /// <summary>
    ///     One line of the trade as it moved: an item's quantity, or coins, from its giver to its receiver.
    /// </summary>
    public sealed class Movement
    {
        public Movement(
            int line,
            long giverId,
            long receiverId,
            long? givenRowId,
            string? itemDefinitionId,
            int quantity,
            long coins,
            Holding? received)
        {
            Line = line;
            GiverId = giverId;
            ReceiverId = receiverId;
            GivenRowId = givenRowId;
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
            Coins = coins;
            Received = received;
        }

        /// <summary>
        ///     The line's number in the trade, from 1, part of its ledger rows' operation IDs.
        /// </summary>
        public int Line { get; }

        public long GiverId { get; }

        public long ReceiverId { get; }

        public long? GivenRowId { get; }

        public string? ItemDefinitionId { get; }

        public int Quantity { get; }

        public long Coins { get; }

        /// <summary>
        ///     The row the line's item ended in; null for coins.
        /// </summary>
        public Holding? Received { get; }
    }
}
}
