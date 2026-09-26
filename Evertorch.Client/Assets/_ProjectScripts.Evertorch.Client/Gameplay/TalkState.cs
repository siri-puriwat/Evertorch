using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The local side of talking to an NPC (Gameplay Systems §6.1): it walks the character to a place to stand beside
///     the NPC with ordinary movement and then asks for the NPC's window. It sends nothing; every command the window
///     sends names the NPC and is checked alone by the server.
/// </summary>
public sealed class TalkState
{
    /// <summary>
    ///     How close to the NPC the window opens without walking on: inside the server's range of 3 m, so the drawn
    ///     position of the character trailing the predicted one does not matter.
    /// </summary>
    public const float OpenDistance = 2.5f;

    private readonly ClientWorld m_world;
    private readonly MovementController m_controller;
    private readonly List<WorldPosition> m_places = new();
    private bool m_isWalking;

    public TalkState(ClientWorld world, MovementController controller)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
        m_world.RemoteDespawned += OnRemoteDespawned;
    }

    public bool IsActive { get; private set; }

    public EntityId Npc { get; private set; }

    /// <summary>
    ///     The character has arrived beside the NPC: its window should open.
    /// </summary>
    public event Action<EntityId>? Arrived;

    /// <summary>
    ///     Starts walking up to <paramref name="npc" />, an NPC this client has a spawn for.
    /// </summary>
    public void Talk(EntityId npc)
    {
        if (m_world.IsLocalDead
            || !m_world.Remotes.TryGetValue(npc, out RemoteEntity? remote)
            || remote.Kind != EntityKind.Npc)
        {
            return;
        }

        End();
        Npc = npc;
        IsActive = true;
        m_controller.CancelPath();
    }

    /// <summary>
    ///     The player asked for something else: a walk of their own, an attack, a pickup, or a skill.
    /// </summary>
    public void Cancel()
    {
        if (IsActive)
        {
            End();
        }
    }

    /// <summary>
    ///     Runs before the movement controller on every client tick.
    /// </summary>
    public void Tick(WorldPosition position)
    {
        if (!IsActive)
        {
            return;
        }

        if (m_world.IsLocalDead || m_controller.HasManualDirection)
        {
            End();
            return;
        }

        if (!m_world.Remotes.TryGetValue(Npc, out RemoteEntity? remote)
            || !remote.Buffer.TrySample(m_world.RemoteRenderTime, out WorldPosition npc, out WorldDirection _))
        {
            End();
            return;
        }

        float distance = HorizontalDistance(position, npc);
        if (distance <= OpenDistance || (m_isWalking && !m_controller.HasPath && distance <= NpcInteraction.Range))
        {
            Arrive();
            return;
        }

        if (m_isWalking && !m_controller.HasPath)
        {
            // The walk ended short of the NPC: the path was abandoned or taken over.
            End();
            return;
        }

        if (!m_isWalking && !TryWalkUp(position, npc))
        {
            End();
        }
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // The NPC's own cell cannot be entered, so the walk heads for the standable cell centre nearest it, the one
    // nearest the player among equals, and the next when no path leads there.
    private bool TryWalkUp(WorldPosition position, WorldPosition npc)
    {
        m_world.Grid.CollectStandingPlaces(npc, NpcInteraction.Range, m_places);
        m_places.Sort((left, right) =>
        {
            int byNpc = GroundDistanceSquared(left, npc).CompareTo(GroundDistanceSquared(right, npc));
            return byNpc != 0
                ? byNpc
                : GroundDistanceSquared(left, position).CompareTo(GroundDistanceSquared(right, position));
        });
        foreach (WorldPosition place in m_places)
        {
            if (m_controller.TryMoveTo(position, place))
            {
                m_isWalking = true;
                return true;
            }
        }

        return false;
    }

    private static float GroundDistanceSquared(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    private void Arrive()
    {
        EntityId npc = Npc;
        End();
        Arrived?.Invoke(npc);
    }

    // Out of view or gone: there is nothing left to walk to.
    private void OnRemoteDespawned(RemoteEntity remote)
    {
        if (IsActive && remote.Entity == Npc)
        {
            End();
        }
    }

    private void End()
    {
        if (IsActive && m_isWalking)
        {
            m_controller.CancelPath();
        }

        IsActive = false;
        Npc = default;
        m_isWalking = false;
    }
}
}
