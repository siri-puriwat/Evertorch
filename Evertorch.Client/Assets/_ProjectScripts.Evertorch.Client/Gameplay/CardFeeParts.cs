using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     Assembles a Smith's removal fees from their parts (Network Protocol §9), one list at a time: a part 0 begins it,
///     the others must follow in order with the same NPC and part count, and no card may come twice. Only a whole list is
///     handed on; a part out of order, of another NPC or count, or with a card already listed drops the list, as do a
///     timeout and the NPC's despawn. A world change needs nothing here: each world has its own.
/// </summary>
public sealed class CardFeeParts
{
    /// <summary>How long a list may wait for its next part; the server sends every part of one in a single tick.</summary>
    public const double TimeoutSeconds = 5;

    private readonly List<CardRemovalFee> m_entries = new();
    private readonly HashSet<ItemDefinitionId> m_cards = new();
    private EntityId m_npc;
    private int m_partCount;
    private int m_nextPart;
    private double m_startedAt;
    private bool m_isAssembling;

    /// <summary>The lists, and the stray parts, dropped before they were whole.</summary>
    public int Dropped { get; private set; }

    public bool IsAssembling => m_isAssembling;

    /// <summary>
    ///     Takes <paramref name="part" />, received at <paramref name="now" /> seconds; true when it completes a list,
    ///     which <paramref name="fees" /> then holds.
    /// </summary>
    public bool TryAdd(CardRemovalFees part, double now, out IReadOnlyList<CardRemovalFee> fees)
    {
        if (part == null)
        {
            throw new ArgumentNullException(nameof(part));
        }

        fees = Array.Empty<CardRemovalFee>();
        if (part.Part == 0)
        {
            if (m_isAssembling)
            {
                Drop();
            }

            m_isAssembling = true;
            m_npc = part.Npc;
            m_partCount = part.PartCount;
            m_startedAt = now;
        }
        else if (!m_isAssembling || part.Npc != m_npc || part.PartCount != m_partCount || part.Part != m_nextPart)
        {
            Drop();
            return false;
        }

        foreach (CardRemovalFee entry in part.Entries)
        {
            if (!m_cards.Add(entry.Card))
            {
                Drop();
                return false;
            }

            m_entries.Add(entry);
        }

        m_nextPart++;
        if (m_nextPart < m_partCount)
        {
            return false;
        }

        fees = m_entries.ToArray();
        Reset();
        return true;
    }

    /// <summary>Drops a list that has waited past <see cref="TimeoutSeconds" /> for its next part.</summary>
    public void Advance(double now)
    {
        if (m_isAssembling && now - m_startedAt > TimeoutSeconds)
        {
            Drop();
        }
    }

    /// <summary>Drops a list of <paramref name="npc" />, which left the world before it was whole.</summary>
    public void Forget(EntityId npc)
    {
        if (m_isAssembling && m_npc == npc)
        {
            Drop();
        }
    }

    private void Drop()
    {
        Dropped++;
        Reset();
    }

    private void Reset()
    {
        m_entries.Clear();
        m_cards.Clear();
        m_npc = default;
        m_partCount = 0;
        m_nextPart = 0;
        m_startedAt = 0;
        m_isAssembling = false;
    }
}
}
