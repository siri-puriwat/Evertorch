using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The client's copy of the map instance it entered: the predicted local player and the remote entities the server
///     has announced. It only ever follows the server; nothing here decides an outcome.
/// </summary>
public sealed class ClientWorld
{
    private readonly Dictionary<EntityId, RemoteEntity> m_remotes = new();
    private readonly double m_tickSeconds;

    public ClientWorld(NavigationGrid grid, WorldEntered entered, uint serverTickRate)
    {
        if (entered == null)
        {
            throw new ArgumentNullException(nameof(entered));
        }

        if (serverTickRate == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(serverTickRate));
        }

        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        Map = entered.Map;
        MapInstance = entered.MapInstance;
        LocalEntity = entered.LocalEntity;
        LatestServerTick = entered.ServerTick;
        m_tickSeconds = 1.0 / serverTickRate;
        Predictor = new MovementPredictor(
            grid,
            entered.MovementSpeed,
            (float)m_tickSeconds,
            entered.Position,
            entered.Facing);
        Smoother = new RenderSmoother(entered.Position);
        ServerTime.Observe(entered.ServerTick * m_tickSeconds);
    }

    public NavigationGrid Grid { get; }

    public MapDefinitionId Map { get; }

    public uint MapInstance { get; }

    public EntityId LocalEntity { get; }

    public MovementPredictor Predictor { get; }

    public RenderSmoother Smoother { get; }

    public ServerTimeEstimator ServerTime { get; } = new();

    public IReadOnlyDictionary<EntityId, RemoteEntity> Remotes => m_remotes;

    public uint LatestServerTick { get; private set; }

    public int SnapshotsApplied { get; private set; }

    public int StaleSnapshots { get; private set; }

    public int UnknownEntityStates { get; private set; }

    public double RemoteRenderTime => ServerTime.Now - RemoteEntityBuffer.InterpolationDelaySeconds;

    public event Action<RemoteEntity>? RemoteSpawned;

    public event Action<RemoteEntity>? RemoteDespawned;

    public void OnSpawn(EntitySpawn spawn)
    {
        if (spawn == null)
        {
            throw new ArgumentNullException(nameof(spawn));
        }

        if (spawn.Entity == LocalEntity)
        {
            return;
        }

        if (m_remotes.TryGetValue(spawn.Entity, out RemoteEntity? replaced))
        {
            m_remotes.Remove(spawn.Entity);
            RemoteDespawned?.Invoke(replaced);
        }

        var remote = new RemoteEntity(spawn.Entity, spawn.Kind, spawn.DefinitionId, spawn.StateFlags);
        remote.Buffer.Add(LatestServerTick * m_tickSeconds, spawn.Position, spawn.Facing);
        m_remotes.Add(spawn.Entity, remote);
        RemoteSpawned?.Invoke(remote);
    }

    public void OnDespawn(EntityDespawn despawn)
    {
        if (m_remotes.TryGetValue(despawn.Entity, out RemoteEntity? remote))
        {
            m_remotes.Remove(despawn.Entity);
            RemoteDespawned?.Invoke(remote);
        }
    }

    public void OnSnapshot(EntitySnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        // Snapshots are unreliable, so an older one can arrive late. A large area is split across several
        // snapshots with the same tick, which is why an equal tick is still applied.
        if (unchecked((int)(snapshot.ServerTick - LatestServerTick)) < 0)
        {
            StaleSnapshots++;
            return;
        }

        LatestServerTick = snapshot.ServerTick;
        SnapshotsApplied++;
        double serverTime = snapshot.ServerTick * m_tickSeconds;
        ServerTime.Observe(serverTime);

        foreach (EntityState state in snapshot.Entities)
        {
            if (state.Entity == LocalEntity)
            {
                WorldPosition before = Predictor.Position;
                Predictor.Reconcile(state, snapshot.LastProcessedInputSequence);
                Smoother.OnCorrected(before, Predictor.Position);
            }
            else if (m_remotes.TryGetValue(state.Entity, out RemoteEntity? remote))
            {
                remote.StateFlags = state.StateFlags;
                remote.Buffer.Add(serverTime, state.Position, state.Facing);
            }
            else
            {
                // The spawn travels on the reliable stream and may still be on its way.
                UnknownEntityStates++;
            }
        }
    }

    public void Advance(float deltaSeconds)
    {
        ServerTime.Advance(deltaSeconds);
        Smoother.Advance(deltaSeconds);
    }
}
}
