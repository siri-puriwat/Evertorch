using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     The character's inventory as last committed, its revision, its coins, and the row each equipment slot holds
///     (Persistence §5). The database is the authority: this copy changes only when a committed change comes back, never
///     ahead of it.
/// </summary>
public sealed class CharacterInventory
{
    private const string WeaponSlotName = "Weapon";
    private const string ArmorSlotName = "Armor";

    private readonly List<InventoryEntry> m_rows = new();

    public CharacterInventory(uint revision, IEnumerable<InventoryEntry> rows)
    {
        Revision = revision;
        m_rows.AddRange(rows);
    }

    public uint Revision { get; set; }

    /// <summary>
    ///     The character's coins at <see cref="Revision" /> (Gameplay Systems §11.3).
    /// </summary>
    public long Coins { get; private set; }

    public IReadOnlyList<InventoryEntry> Rows => m_rows;

    /// <summary>
    ///     The inventory of a loaded character, whose item IDs the content was already checked to define.
    /// </summary>
    public static CharacterInventory FromStored(StoredCharacter stored)
    {
        var inventory = new CharacterInventory(stored.InventoryRevision, Array.Empty<InventoryEntry>())
        {
            Coins = stored.Coins
        };
        foreach (StoredItem item in stored.Items)
        {
            inventory.Take(item);
        }

        return inventory;
    }

    /// <summary>
    ///     The slot a stored row is worn in, by the database's name for it (Persistence §4);
    ///     <see cref="EquipmentSlot.None" /> when it is not worn.
    /// </summary>
    public static EquipmentSlot SlotOf(StoredItem item)
    {
        return item.EquippedSlot switch
        {
            null => EquipmentSlot.None,
            WeaponSlotName => EquipmentSlot.Weapon,
            ArmorSlotName => EquipmentSlot.Armor,
            _ => throw new InvalidOperationException($"Row {item.Id} is worn in an unknown slot '{item.EquippedSlot}'.")
        };
    }

    /// <summary>
    ///     The database's name for <paramref name="slot" /> (Persistence §4).
    /// </summary>
    public static string StoredNameOf(EquipmentSlot slot)
    {
        return slot switch
        {
            EquipmentSlot.Weapon => WeaponSlotName,
            EquipmentSlot.Armor => ArmorSlotName,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot,
                "Only the weapon and armor slots are stored.")
        };
    }

    public bool TryGetRow(long inventoryItem, out InventoryEntry row)
    {
        int index = m_rows.FindIndex(existing => existing.InventoryItem == inventoryItem);
        row = index >= 0 ? m_rows[index] : default;
        return index >= 0;
    }

    /// <summary>
    ///     The row worn in <paramref name="slot" />, or 0 while the slot is empty.
    /// </summary>
    public long WornIn(EquipmentSlot slot)
    {
        if (slot != EquipmentSlot.None)
        {
            foreach (InventoryEntry row in m_rows)
            {
                if (row.Slot == slot)
                {
                    return row.InventoryItem;
                }
            }
        }

        return 0;
    }

    /// <summary>
    ///     Takes a committed change: every row it changed as it is now, with its slot, and the coins, at its revision. A
    ///     row the change emptied, with a quantity of 0, is gone, as the client removes it too (Network Protocol §9).
    ///     Returns the rows as the owner is told them.
    /// </summary>
    public InventoryEntry[] Apply(InventoryResult result)
    {
        var rows = new InventoryEntry[result.Rows.Count];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = Take(result.Rows[index]);
        }

        Revision = result.InventoryRevision;
        Coins = result.Coins;
        return rows;
    }

    public IReadOnlyList<InventorySnapshot> CreateSnapshot()
    {
        return InventorySnapshot.CreateParts(Revision, (uint)Coins, m_rows);
    }

    private InventoryEntry Take(StoredItem stored)
    {
        var row = new InventoryEntry(
            stored.Id,
            new ItemDefinitionId(stored.ItemDefinitionId),
            (uint)stored.Quantity,
            stored.Quantity == 0 ? EquipmentSlot.None : SlotOf(stored));
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

        return row;
    }
}
}
