using System;
using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The account's storage as the server last read it to this character (Gameplay Systems §11.4). A read is taken once
///     all its parts have arrived; a change applies only to the revision it was made from, and any other means another
///     character of the account changed it, or something was missed, so storage must be read again, as it must after a
///     refused deposit or withdrawal.
/// </summary>
public sealed class ClientStorage
{
    private readonly List<StorageEntry> m_rows = new();
    private readonly List<StorageEntry> m_assembly = new();
    private int m_nextPart;

    public IReadOnlyList<StorageEntry> Rows => m_rows;

    public uint Revision { get; private set; }

    /// <summary>
    ///     What a deposit costs at the Storekeeper that answered the last read.
    /// </summary>
    public uint DepositFee { get; private set; }

    /// <summary>
    ///     A whole read has been taken and nothing missed since; until then changes are ignored.
    /// </summary>
    public bool IsCurrent { get; private set; }

    /// <summary>
    ///     A read was asked for and has not been answered.
    /// </summary>
    public bool IsReading { get; private set; }

    /// <summary>
    ///     Goes up with every change of what is known, so a window can tell when to show it again.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    ///     Raised after each committed change is applied, with what it moved.
    /// </summary>
    public event Action<StorageDelta>? ChangeApplied;

    public void BeginRead()
    {
        IsReading = true;
        Version++;
    }

    public void OnSnapshot(StorageSnapshot part)
    {
        if (part == null)
        {
            throw new ArgumentNullException(nameof(part));
        }

        if (part.Part == 0)
        {
            m_assembly.Clear();
            m_nextPart = 0;
        }
        else if (part.Part != m_nextPart)
        {
            // A part out of order spoils the read; the window asks again.
            m_assembly.Clear();
            m_nextPart = 0;
            IsReading = false;
            IsCurrent = false;
            Version++;
            return;
        }

        m_assembly.AddRange(part.Entries);
        m_nextPart++;
        if (!part.IsLast)
        {
            return;
        }

        m_rows.Clear();
        m_rows.AddRange(m_assembly);
        m_assembly.Clear();
        m_nextPart = 0;
        Revision = part.Revision;
        DepositFee = part.DepositFee;
        IsCurrent = true;
        IsReading = false;
        Version++;
    }

    public void OnChanged(StorageChanged change)
    {
        if (change == null)
        {
            throw new ArgumentNullException(nameof(change));
        }

        if (!IsCurrent)
        {
            return;
        }

        if (change.PriorRevision != Revision)
        {
            IsCurrent = false;
            Version++;
            return;
        }

        StorageEntry row = change.Row;
        int index = m_rows.FindIndex(entry => entry.StorageItem == row.StorageItem);
        uint before = index >= 0 ? m_rows[index].Quantity : 0;
        if (row.Quantity == 0)
        {
            if (index >= 0)
            {
                m_rows.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            m_rows[index] = row;
        }
        else
        {
            m_rows.Add(row);
        }

        Revision = change.NewRevision;
        Version++;
        ChangeApplied?.Invoke(new StorageDelta(row.Item, (long)row.Quantity - before, DepositFee));
    }

    /// <summary>
    ///     A refused read ends the wait for it; a refused deposit or withdrawal means what is shown may be out of date.
    /// </summary>
    public void OnRefused(StorageCommand command)
    {
        if (command == StorageCommand.Open)
        {
            IsReading = false;
        }
        else
        {
            IsCurrent = false;
        }

        Version++;
    }
}
}
