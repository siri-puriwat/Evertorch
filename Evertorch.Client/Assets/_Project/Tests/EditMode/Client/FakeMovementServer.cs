using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The server's movement contract in miniature: newest-sequence-wins, one input per tick, a short hold when none
///     arrives, the shared movement step, and a snapshot that acknowledges the last input applied.
/// </summary>
internal sealed class FakeMovementServer
{
    private const int HoldTicks = 5;

    private readonly NavigationGrid m_grid;
    private readonly Queue<MoveIntent> m_queue = new();
    private readonly EntityId m_entity;
    private WorldDirection m_heldDirection;
    private int m_ticksWithoutInput;
    private bool m_hasReceived;
    private uint m_newestReceived;

    public FakeMovementServer(NavigationGrid grid, EntityId entity, WorldPosition position)
    {
        m_grid = grid;
        m_entity = entity;
        Position = position;
        Facing = new WorldDirection(0f, 1f);
    }

    public uint Tick { get; private set; }

    public WorldPosition Position { get; private set; }

    public WorldDirection Facing { get; private set; }

    public uint LastApplied { get; private set; }

    public int StaleInputs { get; private set; }

    public void Receive(MoveIntent intent)
    {
        if (m_hasReceived && unchecked((int)(intent.Sequence - m_newestReceived)) <= 0)
        {
            StaleInputs++;
            return;
        }

        m_hasReceived = true;
        m_newestReceived = intent.Sequence;
        m_queue.Enqueue(intent);
    }

    public EntitySnapshot Step()
    {
        Tick++;
        if (m_queue.Count > 0)
        {
            MoveIntent intent = m_queue.Dequeue();
            m_heldDirection = new WorldDirection(intent.DirectionX, intent.DirectionZ);
            m_ticksWithoutInput = 0;
            LastApplied = intent.Sequence;
        }
        else if (++m_ticksWithoutInput > HoldTicks)
        {
            m_heldDirection = default;
        }

        MovementStep step = MovementModel.Step(
            m_grid,
            Position,
            Facing,
            m_heldDirection,
            ClientTestGrids.Speed,
            ClientTestGrids.TickSeconds);
        Position = step.Position;
        Facing = step.Facing;
        var state = new EntityState(
            m_entity,
            Position,
            Facing,
            step.VelocityX,
            step.VelocityY,
            step.VelocityZ,
            step.IsMoving ? EntityStateFlags.Moving : EntityStateFlags.None);
        return new EntitySnapshot(Tick, LastApplied, new[] { state });
    }
}
}
