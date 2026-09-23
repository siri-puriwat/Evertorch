using System;
using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The local character's inventory as the server last committed it (Network Protocol §9, §12). A snapshot is
///     applied only once all its parts have arrived; a change applies only to the revision it was made from, and any
///     other revision means something was missed, so the whole inventory is asked for again.
/// </summary>
public sealed class ClientInventory
{
    private readonly List<InventoryEntry> m_rows = new();
    private readonly List<InventoryEntry> m_assembly = new();
    private uint m_assemblyRevision;
    private int m_nextPart;
    private int m_partCount;
    private bool m_isAwaitingResync;

    public IReadOnlyList<InventoryEntry> Rows => m_rows;

    public uint Revision { get; private set; }

    /// <summary>
    ///     A whole snapshot has been applied and no gap has been seen since; until then changes are ignored.
    /// </summary>
    public bool IsCurrent { get; private set; }

    public int Resyncs { get; private set; }

    public int IgnoredChanges { get; private set; }

    public event Action? Changed;

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
            m_partCount = part.PartCount;
            m_nextPart = 0;
        }
        else if (m_nextPart != part.Part || m_assemblyRevision != part.Revision || m_partCount != part.PartCount)
        {
            // A part of another snapshot discards the unfinished one.
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

        foreach (InventoryEntry entry in change.Changes)
        {
            int index = m_rows.FindIndex(row => row.InventoryItem == entry.InventoryItem);
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
        Changed?.Invoke();
        return false;
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
