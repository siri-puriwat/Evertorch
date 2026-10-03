using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     One trader's side of an open trade (Gameplay Systems §16): how much of each of its rows it offers, in the order
///     they were offered, its coins, and whether it is locked and confirmed.
/// </summary>
public sealed class TradeOffering
{
    private readonly List<long> m_order = new();
    private readonly Dictionary<long, uint> m_rows = new();

    public uint Coins { get; set; }

    public bool IsLocked { get; set; }

    public bool IsConfirmed { get; set; }

    public int RowCount => m_order.Count;

    public bool IsEmpty => m_order.Count == 0 && Coins == 0;

    /// <summary>
    ///     Each offered row and its quantity, in the order offered.
    /// </summary>
    public IEnumerable<(long Row, uint Quantity)> Rows
    {
        get
        {
            foreach (long row in m_order)
            {
                yield return (row, m_rows[row]);
            }
        }
    }

    public bool Offers(long row)
    {
        return m_rows.ContainsKey(row);
    }

    /// <summary>
    ///     Offers <paramref name="quantity" /> of the row; 0 takes it back.
    /// </summary>
    public void Set(long row, uint quantity)
    {
        if (quantity == 0)
        {
            m_rows.Remove(row);
            m_order.Remove(row);
            return;
        }

        if (!m_rows.ContainsKey(row))
        {
            m_order.Add(row);
        }

        m_rows[row] = quantity;
    }

    /// <summary>
    ///     The side as a trader sees it: its owner's with the rows' IDs, the partner's without them. A row the owner no
    ///     longer holds is left out.
    /// </summary>
    public TradeSide ToMessage(CharacterInventory inventory, TradeSideOwner seenAs)
    {
        var entries = new List<TradeEntry>(m_order.Count);
        foreach (long row in m_order)
        {
            if (inventory.TryGetRow(row, out InventoryEntry held))
            {
                entries.Add(new TradeEntry(
                    seenAs == TradeSideOwner.Own ? row : 0,
                    held.Item,
                    m_rows[row],
                    held.RefineLevel));
            }
        }

        return new TradeSide(seenAs, IsLocked, IsConfirmed, Coins, entries);
    }
}
}
