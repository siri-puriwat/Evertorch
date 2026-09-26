using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     The character's inventory as last committed, and its revision (Persistence §5). The database is the authority:
///     this copy changes only when a committed change comes back, never ahead of it.
/// </summary>
public sealed class CharacterInventory
{
    private readonly List<InventoryEntry> m_rows = new();

    public CharacterInventory(uint revision, IEnumerable<InventoryEntry> rows)
    {
        Revision = revision;
        m_rows.AddRange(rows);
    }

    public uint Revision { get; set; }

    public IReadOnlyList<InventoryEntry> Rows => m_rows;

    /// <summary>
    ///     The inventory of a loaded character, whose item IDs the content was already checked to define.
    /// </summary>
    public static CharacterInventory FromStored(StoredCharacter stored)
    {
        var rows = new List<InventoryEntry>(stored.Items.Count);
        foreach (StoredItem item in stored.Items)
        {
            rows.Add(new InventoryEntry(item.Id, new ItemDefinitionId(item.ItemDefinitionId), (uint)item.Quantity));
        }

        return new CharacterInventory(stored.InventoryRevision, rows);
    }

    /// <summary>
    ///     Takes a committed change: <paramref name="row" /> as it is now, at <paramref name="revision" />. A row the
    ///     change emptied, with a quantity of 0, is gone, as the client removes it too (Network Protocol §9).
    /// </summary>
    public void Apply(uint revision, InventoryEntry row)
    {
        int index = m_rows.FindIndex(existing => existing.InventoryItem == row.InventoryItem);
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

        Revision = revision;
    }

    public IReadOnlyList<InventorySnapshot> CreateSnapshot()
    {
        return InventorySnapshot.CreateParts(Revision, m_rows);
    }
}
}
