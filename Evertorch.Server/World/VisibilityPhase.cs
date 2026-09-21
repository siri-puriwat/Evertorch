using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
/// After the world has settled for this tick, tells each client which entities entered and left its area of
/// interest. Spawns go out on the reliable control stream, so they follow that client's <see cref="WorldEntered"/>.
/// </summary>
public sealed class VisibilityPhase : ITickPhase
{
    private readonly SessionRegistry m_sessions;
    private readonly MessageSender m_sender;
    private readonly List<PlayerEntity> m_visible = new List<PlayerEntity>();
    private readonly HashSet<EntityId> m_visibleIds = new HashSet<EntityId>();
    private readonly List<EntityId> m_departed = new List<EntityId>();

    public VisibilityPhase(SessionRegistry sessions, MessageSender sender)
    {
        m_sessions = sessions;
        m_sender = sender;
    }

    public TickPhase Phase => TickPhase.FinalizeWorld;

    public void Execute(in TickContext context)
    {
        // Every player is moved to its current cell before anyone looks around. Doing both in one pass would let
        // an observer see a neighbour in the cell it stood in last tick.
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Player != null && session.Map != null)
            {
                session.Map.Interest.Update(session.Player);
            }
        }

        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Player != null && session.Map != null)
            {
                Update(session, session.Player, session.Map);
            }
        }
    }

    private void Update(ClientSession session, PlayerEntity observer, MapInstance map)
    {
        m_visible.Clear();
        m_visibleIds.Clear();
        map.Interest.CollectVisible(observer, m_visible);

        foreach (PlayerEntity entity in m_visible)
        {
            m_visibleIds.Add(entity.Id);
            if (session.KnownEntities.Add(entity.Id))
            {
                m_sender.Send(
                    session.Connection,
                    new EntitySpawn(
                        entity.Id,
                        EntityKind.Player,
                        entity.Job.Value,
                        entity.Position,
                        entity.Facing,
                        entity.StateFlags));
            }
        }

        m_departed.Clear();
        foreach (EntityId known in session.KnownEntities)
        {
            if (!m_visibleIds.Contains(known))
            {
                m_departed.Add(known);
            }
        }

        foreach (EntityId departed in m_departed)
        {
            session.KnownEntities.Remove(departed);
            DespawnReason reason = map.Contains(departed) ? DespawnReason.OutOfRange : DespawnReason.Removed;
            m_sender.Send(session.Connection, new EntityDespawn(departed, reason));
        }
    }
}
}
