using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of an NPC: its name and the logical key of its view. What it sells and the quests it gives
///     arrive on the wire (Content Pipeline §5).
/// </summary>
public sealed class ClientNpc
{
    public ClientNpc(NpcDefinitionId id, string displayName, string prefabKey)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PrefabKey = prefabKey ?? throw new ArgumentNullException(nameof(prefabKey));
    }

    public NpcDefinitionId Id { get; }

    public string DisplayName { get; }

    public string PrefabKey { get; }
}
}
