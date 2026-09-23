using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     After the world has settled for this tick, tells each client which entities entered and left its area of
///     interest. Spawns go out on the reliable control stream, so they follow that client's <see cref="WorldEntered" />.
/// </summary>
public sealed class VisibilityPhase : ITickPhase
{
    private readonly SessionRegistry m_sessions;
    private readonly WorldSimulation m_world;
    private readonly MessageSender m_sender;
    private readonly Targeting m_targeting;
    private readonly List<WorldEntity> m_visible = new();
    private readonly HashSet<EntityId> m_visibleIds = new();
    private readonly List<EntityId> m_departed = new();

    public VisibilityPhase(SessionRegistry sessions, WorldSimulation world, MessageSender sender, Targeting targeting)
    {
        m_sessions = sessions;
        m_world = world;
        m_sender = sender;
        m_targeting = targeting;
    }

    public TickPhase Phase => TickPhase.FinalizeWorld;

    public void Execute(in TickContext context)
    {
        // Every entity is moved to its current cell before anyone looks around. Doing both in one pass would let
        // an observer see a neighbour in the cell it stood in last tick.
        foreach (MapInstance map in m_world.Maps)
        {
            foreach (WorldEntity entity in map.Entities)
            {
                map.Interest.Update(entity);
            }
        }

        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Player != null && session.Map != null)
            {
                Update(session, session.Player, session.Map, context.Tick);
            }
        }
    }

    private void Update(ClientSession session, PlayerEntity observer, MapInstance map, uint tick)
    {
        m_visible.Clear();
        m_visibleIds.Clear();
        map.Interest.CollectVisible(observer, m_visible);

        foreach (WorldEntity entity in m_visible)
        {
            m_visibleIds.Add(entity.Id);
            if (session.KnownEntities.Add(entity.Id))
            {
                m_sender.Send(
                    session.Connection,
                    new EntitySpawn(
                        entity.Id,
                        entity.Kind,
                        entity.DefinitionId,
                        entity.Position,
                        entity.Facing,
                        entity.StateFlags,
                        entity.SharedHealthPermille));
                if (entity is ItemDropEntity drop && drop.DroppedTick == tick)
                {
                    // Only a client that sees the drop land is told what fell; one that walks up later sees the item.
                    m_sender.Send(
                        session.Connection,
                        new ItemDropped(drop.Id, drop.DefinitionId, drop.Amount, drop.Position));
                }
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
            m_targeting.ClearIfTargeting(session, departed);
        }
    }
}
}
