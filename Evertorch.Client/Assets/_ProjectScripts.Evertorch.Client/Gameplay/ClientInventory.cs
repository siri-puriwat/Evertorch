using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The local character's inventory and coins as the server last committed them (Network Protocol §9, §12). A
///     snapshot is applied only once all its parts have arrived; a change applies only to the revision it was made from,
///     and any other revision means something was missed, so the whole inventory is asked for again.
/// </summary>
public sealed class ClientInventory
{
    private readonly List<InventoryEntry> m_rows = new();
    private readonly List<InventoryEntry> m_assembly = new();
    private uint m_assemblyRevision;
    private uint m_assemblyCoins;
    private int m_nextPart;
    private int m_partCount;
    private bool m_isAwaitingResync;

    public IReadOnlyList<InventoryEntry> Rows => m_rows;

    public uint Revision { get; private set; }

    /// <summary>
    ///     The character's coins at <see cref="Revision" />.
    /// </summary>
    public uint Coins { get; private set; }

    /// <summary>
    ///     A whole snapshot has been applied and no gap has been seen since; until then changes are ignored.
    /// </summary>
    public bool IsCurrent { get; private set; }

    public int Resyncs { get; private set; }

    public int IgnoredChanges { get; private set; }

    public event Action? Changed;

    /// <summary>
    ///     Raised after each committed change is applied, with what it did. A snapshot raises only
    ///     <see cref="Changed" />: after a gap nobody can tell what was missed.
    /// </summary>
    public event Action<InventoryDelta>? ChangeApplied;

    /// <summary>
    ///     Takes one snapshot part. True when the parts arrived out of order, which means the client must ask for the
    ///     inventory again.
    /// </summary>
    public bool OnSnapshot(InventorySnapshot part)
    {
        if (part == null)
        {
            throw new ArgumentNullException(nameof(part));
        }

        if (part.Part == 0)
        {
            m_assembly.Clear();
            m_assemblyRevision = part.Revision;
            m_assemblyCoins = part.Coins;
            m_partCount = part.PartCount;
            m_nextPart = 0;
        }
        else if (m_nextPart != part.Part
                 || m_assemblyRevision != part.Revision
                 || m_partCount != part.PartCount
                 || m_assemblyCoins != part.Coins)
        {
            // A part of another snapshot discards the unfinished one; parts of one snapshot carry the same coins.
            m_assembly.Clear();
            m_nextPart = 0;
            m_partCount = 0;
            return RequestResync();
        }

        m_assembly.AddRange(part.Entries);
        m_nextPart++;
        if (!part.IsLast)
        {
            return false;
        }

        m_rows.Clear();
        m_rows.AddRange(m_assembly);
        m_assembly.Clear();
        m_nextPart = 0;
        m_partCount = 0;
        Revision = part.Revision;
        Coins = m_assemblyCoins;
        IsCurrent = true;
        m_isAwaitingResync = false;
        Changed?.Invoke();
        return false;
    }

    /// <summary>
    ///     Applies one committed change. True when it was made from another revision than this one, which means the
    ///     client must ask for the inventory again.
    /// </summary>
    public bool OnChanged(InventoryChanged change)
    {
        if (change == null)
        {
            throw new ArgumentNullException(nameof(change));
        }

        if (!IsCurrent)
        {
            IgnoredChanges++;
            return false;
        }

        if (change.PriorRevision != Revision)
        {
            IgnoredChanges++;
            return RequestResync();
        }

        uint coinsBefore = Coins;
        var units = new Dictionary<ItemDefinitionId, long>();
        byte refineLevel = 0;
        IReadOnlyList<ItemDefinitionId> cards = Array.Empty<ItemDefinitionId>();
        ItemDefinitionId insertedCard = default;
        InventoryEntry insertedInto = default;
        foreach (InventoryEntry entry in change.Changes)
        {
            int index = m_rows.FindIndex(row => row.InventoryItem == entry.InventoryItem);
            if (index >= 0)
            {
                Count(units, m_rows[index].Item, -m_rows[index].Quantity);
            }

            // An emptied row arrives without its level and its cards, so the row held names them.
            if ((index >= 0 ? m_rows[index].Quantity : 0) != entry.Quantity)
            {
                refineLevel = Math.Max(refineLevel, index >= 0 ? m_rows[index].RefineLevel : entry.RefineLevel);
                IReadOnlyList<ItemDefinitionId> held = index >= 0 ? m_rows[index].Cards : entry.Cards;
                if (cards.Count == 0)
                {
                    cards = held;
                }
            }

            // A row whose cards grew took a card, which the words name (Prototype Content §2).
            if (index >= 0 && entry.Cards.Count > m_rows[index].Cards.Count)
            {
                insertedCard = entry.Cards[entry.Cards.Count - 1];
                insertedInto = entry;
            }

            Count(units, entry.Item, entry.Quantity);
            if (entry.Quantity == 0)
            {
                if (index >= 0)
                {
                    m_rows.RemoveAt(index);
                }
            }
            else if (index >= 0)
            {
                m_rows[index] = entry;
            }
            else
            {
                m_rows.Add(entry);
            }
        }

        Revision = change.NewRevision;
        Coins = change.Coins;
        Changed?.Invoke();
        ChangeApplied?.Invoke(
            new InventoryDelta(coinsBefore, Coins, units, refineLevel, cards, insertedCard, insertedInto));
        return false;
    }

    private static void Count(Dictionary<ItemDefinitionId, long> units, ItemDefinitionId item, long added)
    {
        units.TryGetValue(item, out long held);
        held += added;
        if (held == 0)
        {
            units.Remove(item);
        }
        else
        {
            units[item] = held;
        }
    }

    // One request per gap: the snapshot that answers it covers everything missed until then.
    private bool RequestResync()
    {
        IsCurrent = false;
        if (m_isAwaitingResync)
        {
            return false;
        }

        m_isAwaitingResync = true;
        Resyncs++;
        return true;
    }
}
}
