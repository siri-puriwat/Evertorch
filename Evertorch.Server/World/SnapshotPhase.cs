using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Sends each client the transforms of its own entity and of the entities it has been told about. It runs after
///     visibility, so a snapshot never mentions an entity whose spawn has not been queued for that client.
/// </summary>
public sealed class SnapshotPhase : ITickPhase
{
    private readonly SessionRegistry m_sessions;
    private readonly MessageSender m_sender;
    private readonly uint m_intervalTicks;
    private readonly List<EntityState> m_states = new();
    private readonly List<EntityState> m_packet = new(EntitySnapshot.MaxEntities);

    public SnapshotPhase(SessionRegistry sessions, MessageSender sender, IOptions<WorldOptions> world)
    {
        m_sessions = sessions;
        m_sender = sender;
        m_intervalTicks = (uint)world.Value.SnapshotIntervalTicks;
    }

    public TickPhase Phase => TickPhase.BuildSnapshots;

    public void Execute(in TickContext context)
    {
        if (context.Tick % m_intervalTicks != 0)
        {
            return;
        }

        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Player != null && session.Map != null)
            {
                Send(session, session.Player, session.Map, context.Tick);
            }
        }
    }

    private static EntityState ToState(WorldEntity entity)
    {
        return new EntityState(
            entity.Id,
            entity.Position,
            entity.Facing,
            entity.VelocityX,
            entity.VelocityY,
            entity.VelocityZ,
            entity.StateFlags);
    }

    private void Send(ClientSession session, PlayerEntity own, MapInstance map, uint tick)
    {
        // The client's own entity leads the first packet: it is the state prediction is reconciled against.
        m_states.Clear();
        m_states.Add(ToState(own));
        foreach (EntityId known in session.KnownEntities)
        {
            // A drop or an NPC never moves; its spawn said everything about it.
            if (map.TryGetEntity(known, out WorldEntity? other)
                && other != null
                && other.Kind != EntityKind.ItemDrop
                && other.Kind != EntityKind.Npc)
            {
                m_states.Add(ToState(other));
            }
        }

        uint acknowledged = session.Input != null ? session.Input.LastProcessedSequence : 0;
        for (int offset = 0; offset < m_states.Count; offset += EntitySnapshot.MaxEntities)
        {
            m_packet.Clear();
            int end = Math.Min(offset + EntitySnapshot.MaxEntities, m_states.Count);
            for (int index = offset; index < end; index++)
            {
                m_packet.Add(m_states[index]);
            }

            m_sender.Send(session.Connection, new EntitySnapshot(tick, acknowledged, m_packet));
        }
    }
}
}
