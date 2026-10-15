using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     Puts an inspection's parts together (Network Protocol §9): one answer is awaited at most, by the request's
///     sequence, and nothing is kept of one that is incomplete, mixed, duplicated, or late. A part of another request,
///     another player, another revision or count, a part or a position seen twice, or the eight positions not each
///     present once drop the answer; a timeout or a world change drops it too.
/// </summary>
public sealed class InspectionParts
{
    /// <summary>How long an answer may take, in seconds, before the client stops waiting for it.</summary>
    public const double TimeoutSeconds = 5;

    private readonly InspectedItem[] m_items = new InspectedItem[EquipmentPositions.All.Count];
    private readonly bool[] m_parts = new bool[InspectedEquipment.MaxParts];
    private EntityId m_entity;
    private uint m_sequence;
    private double m_askedAt;
    private int m_positions;
    private int m_partsSeen;
    private int m_partCount;
    private uint m_revision;
    private bool m_isWaiting;

    /// <summary>Answers that were dropped, as incomplete, mixed, duplicated, or late.</summary>
    public int Dropped { get; private set; }

    public bool IsWaiting => m_isWaiting;

    /// <summary>Awaits the answer to request <paramref name="sequence" /> about <paramref name="entity" />.</summary>
    public void Expect(uint sequence, EntityId entity, double now)
    {
        Reset();
        m_sequence = sequence;
        m_entity = entity;
        m_askedAt = now;
        m_isWaiting = true;
    }

    /// <summary>Stops waiting, as a world change does.</summary>
    public void Forget()
    {
        Reset();
    }

    /// <summary>Drops an answer awaited longer than <see cref="TimeoutSeconds" />.</summary>
    public void Advance(double now)
    {
        if (m_isWaiting && now - m_askedAt > TimeoutSeconds)
        {
            Drop();
        }
    }

    /// <summary>
    ///     Takes one part. True with the whole answer when this part completes it; false while it waits for more and when
    ///     the part was dropped.
    /// </summary>
    public bool TryAdd(InspectedEquipment part, out PlayerInspection? whole)
    {
        whole = null;
        if (!m_isWaiting || part.CommandSequence != m_sequence || part.Entity != m_entity)
        {
            Dropped++;
            return false;
        }

        if (m_partsSeen == 0)
        {
            m_partCount = part.PartCount;
            m_revision = part.Revision;
        }

        if (part.PartCount != m_partCount || part.Revision != m_revision || m_parts[part.Part])
        {
            Drop();
            return false;
        }

        m_parts[part.Part] = true;
        m_partsSeen++;
        foreach (InspectedEntry entry in part.Entries)
        {
            int index = (int)entry.Slot - 1;
            int bit = 1 << index;
            if ((m_positions & bit) != 0)
            {
                Drop();
                return false;
            }

            m_positions |= bit;
            m_items[index] = entry.Item;
        }

        if (m_partsSeen < m_partCount)
        {
            return false;
        }

        if (m_positions != (1 << m_items.Length) - 1)
        {
            Drop();
            return false;
        }

        whole = new PlayerInspection(m_entity, m_revision, m_items);
        Reset();
        return true;
    }

    private void Drop()
    {
        Dropped++;
        Reset();
    }

    private void Reset()
    {
        m_isWaiting = false;
        m_positions = 0;
        m_partsSeen = 0;
        m_partCount = 0;
        Array.Clear(m_parts, 0, m_parts.Length);
        Array.Clear(m_items, 0, m_items.Length);
    }
}
}
