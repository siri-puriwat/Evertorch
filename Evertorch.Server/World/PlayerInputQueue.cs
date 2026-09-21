using System;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
/// Movement inputs one client has sent and the simulation has not applied yet. It accepts each sequence at most
/// once and only in increasing order, so a delayed or duplicated packet can never undo a newer input.
/// </summary>
public sealed class PlayerInputQueue
{
    private readonly MoveIntent[] m_inputs;
    private int m_head;
    private bool m_hasReceived;
    private uint m_lastReceivedSequence;

    public PlayerInputQueue(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "The input queue needs room for one input.");
        }

        m_inputs = new MoveIntent[capacity];
    }

    public int Count { get; private set; }

    /// <summary>
    /// Inputs refused because a newer or equal sequence had already arrived. Ordinary on a lossy network.
    /// </summary>
    public long Stale { get; private set; }

    /// <summary>
    /// Oldest inputs discarded because more arrived than one per tick can consume.
    /// </summary>
    public long Dropped { get; private set; }

    public bool TryEnqueue(MoveIntent intent)
    {
        // Signed distance on the unsigned circle keeps the comparison right when the sequence wraps around.
        if (m_hasReceived && (int)(intent.Sequence - m_lastReceivedSequence) <= 0)
        {
            Stale++;
            return false;
        }

        m_hasReceived = true;
        m_lastReceivedSequence = intent.Sequence;

        if (Count == m_inputs.Length)
        {
            // Dropping the oldest keeps latency bounded; it grants nothing, because unapplied input moves nobody.
            m_head = (m_head + 1) % m_inputs.Length;
            Count--;
            Dropped++;
        }

        m_inputs[(m_head + Count) % m_inputs.Length] = intent;
        Count++;
        return true;
    }

    public bool TryDequeue(out MoveIntent intent)
    {
        if (Count == 0)
        {
            intent = default;
            return false;
        }

        intent = m_inputs[m_head];
        m_head = (m_head + 1) % m_inputs.Length;
        Count--;
        return true;
    }
}
}
