using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client
{
/// <summary>
///     The names under the feet (Prototype Content §2): every player's, the local player's included, and every NPC's
///     display name. It only draws what the world already holds.
/// </summary>
public sealed class NamePlatePresenter : IDisposable
{
    private readonly ClientWorld m_world;
    private readonly ClientContent? m_content;
    private readonly string m_localName;
    private readonly Dictionary<EntityId, NamePlate> m_plates = new();
    private NamePlate? m_localPlate;

    public NamePlatePresenter(ClientWorld world, ClientContent? content, string localName)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_content = content;
        m_localName = localName ?? throw new ArgumentNullException(nameof(localName));
        m_world.RemoteDespawned += OnRemoteDespawned;
    }

    public NamePlate? LocalPlate => m_localPlate;

    public void Dispose()
    {
        m_world.RemoteDespawned -= OnRemoteDespawned;
        foreach (NamePlate plate in m_plates.Values)
        {
            Destroy(plate);
        }

        m_plates.Clear();
        Destroy(m_localPlate);
        m_localPlate = null;
    }

    public bool TryGetPlate(EntityId entity, out NamePlate? plate)
    {
        return m_plates.TryGetValue(entity, out plate);
    }

    /// <summary>
    ///     The name shown under <paramref name="remote" />: a player's own, an NPC's display name, or empty for any
    ///     other kind, which shows none.
    /// </summary>
    public static string NameOf(ClientContent? content, RemoteEntity remote)
    {
        if (remote == null)
        {
            throw new ArgumentNullException(nameof(remote));
        }

        switch (remote.Kind)
        {
            case EntityKind.Player:
                return remote.Name;
            case EntityKind.Npc:
                return content != null
                    && content.TryGetNpc(new NpcDefinitionId(remote.DefinitionId), out ClientNpc? npc)
                    && npc != null
                        ? npc.DisplayName
                        : remote.DefinitionId;
            default:
                return string.Empty;
        }
    }

    public void Present(EntityView? local, IReadOnlyDictionary<EntityId, EntityView> remotes, Camera? camera)
    {
        if (remotes == null)
        {
            throw new ArgumentNullException(nameof(remotes));
        }

        if (local != null && m_localName.Length > 0)
        {
            m_localPlate ??= NamePlate.Create(m_localName);
            m_localPlate.Show(local.transform.position, camera);
        }

        foreach (KeyValuePair<EntityId, EntityView> pair in remotes)
        {
            if (!m_world.Remotes.TryGetValue(pair.Key, out RemoteEntity? remote))
            {
                continue;
            }

            string name = NameOf(m_content, remote);
            if (name.Length == 0)
            {
                continue;
            }

            if (!m_plates.TryGetValue(pair.Key, out NamePlate? plate))
            {
                plate = NamePlate.Create(name);
                m_plates.Add(pair.Key, plate);
            }

            plate.SetText(name);
            plate.Show(pair.Value.transform.position, camera);
        }
    }

    private void OnRemoteDespawned(RemoteEntity remote)
    {
        if (m_plates.TryGetValue(remote.Entity, out NamePlate? plate))
        {
            m_plates.Remove(remote.Entity);
            Destroy(plate);
        }
    }

    private static void Destroy(NamePlate? plate)
    {
        if (plate != null)
        {
            Object.Destroy(plate.gameObject);
        }
    }
}
}
