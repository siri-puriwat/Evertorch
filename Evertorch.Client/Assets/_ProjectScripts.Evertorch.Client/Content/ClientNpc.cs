using System;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of an NPC: its name, the logical key of its view, and the colour of its body. What it sells
///     and the quests it gives arrive on the wire (Content Pipeline §5).
/// </summary>
public sealed class ClientNpc
{
    public ClientNpc(NpcDefinitionId id, string displayName, string prefabKey, Color? tint = null)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PrefabKey = prefabKey ?? throw new ArgumentNullException(nameof(prefabKey));
        Tint = tint;
    }

    public NpcDefinitionId Id { get; }

    public string DisplayName { get; }

    public string PrefabKey { get; }

    /// <summary>
    ///     The colour of the whole body; null for the model's own.
    /// </summary>
    public Color? Tint { get; }
}
}
