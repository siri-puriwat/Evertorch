using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
/// Moves the local player ahead of the server with the shared movement model and keeps every input the server has
/// not acknowledged, so a snapshot can be adopted and the remaining inputs replayed on top of it.
/// </summary>
public sealed class MovementPredictor
{
    // More than six seconds of unacknowledged input at 20 Hz; the connection times out well before that.
    public const int MaxPendingInputs = 128;

    private readonly NavigationGrid m_grid;
    private readonly float m_speed;
    private readonly float m_tickSeconds;
    private readonly Queue<MoveIntent> m_pending = new Queue<MoveIntent>();

    public MovementPredictor(
        NavigationGrid grid,
        float speed,
        float tickSeconds,
        WorldPosition position,
        WorldDirection facing)
    {
        m_grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_speed = speed;
        m_tickSeconds = tickSeconds;
        Position = position;
        Facing = facing;
    }

    public WorldPosition Position { get; private set; }

    public WorldDirection Facing { get; private set; }

    public bool IsMoving { get; private set; }

    public float StepDistance => m_speed * m_tickSeconds;

    public int PendingCount => m_pending.Count;

    public uint LastAcknowledgedSequence { get; private set; }

    public int DroppedPendingInputs { get; private set; }

    public void Apply(MoveIntent intent)
    {
        Integrate(intent);
        m_pending.Enqueue(intent);
        if (m_pending.Count > MaxPendingInputs)
        {
            m_pending.Dequeue();
            DroppedPendingInputs++;
        }
    }

    /// <summary>
    /// Adopts the server's state, forgets the inputs it has applied, and replays the rest.
    /// </summary>
    public void Reconcile(EntityState authoritative, uint lastProcessedSequence)
    {
        Position = authoritative.Position;
        Facing = authoritative.Facing;
        IsMoving = (authoritative.StateFlags & EntityStateFlags.Moving) != 0;
        LastAcknowledgedSequence = lastProcessedSequence;

        while (m_pending.Count > 0 && !IsNewer(m_pending.Peek().Sequence, lastProcessedSequence))
        {
            m_pending.Dequeue();
        }

        foreach (MoveIntent pending in m_pending)
        {
            Integrate(pending);
        }
    }

    private static bool IsNewer(uint sequence, uint reference)
    {
        return unchecked((int)(sequence - reference)) > 0;
    }

    private void Integrate(MoveIntent intent)
    {
        MovementStep step = MovementModel.Step(
            m_grid,
            Position,
            Facing,
            new WorldDirection(intent.DirectionX, intent.DirectionZ),
            m_speed,
            m_tickSeconds);
        Position = step.Position;
        Facing = step.Facing;
        IsMoving = step.IsMoving;
    }
}
}
