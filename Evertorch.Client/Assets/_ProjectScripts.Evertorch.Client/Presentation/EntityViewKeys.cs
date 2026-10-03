using Evertorch.Game;
using Evertorch.Protocol;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     Chooses the Addressables key an entity is drawn with from the client content. An empty key means the content
///     does not know the definition, and the view shows its placeholder.
/// </summary>
public static class EntityViewKeys
{
    public static string ForJob(ClientContent? content, JobDefinitionId job)
    {
        return content != null && content.TryGetJob(job, out ClientJob? definition) && definition != null
            ? definition.PrefabKey
            : string.Empty;
    }

    /// <summary>
    ///     The monster the content knows by <paramref name="definitionId" />, whose body's size and colour its view
    ///     takes (Prototype Content §2); null for any other entity, or a monster the content does not know.
    /// </summary>
    public static ClientMonster? MonsterOf(ClientContent? content, EntityKind kind, string definitionId)
    {
        return kind == EntityKind.Monster
            && content != null
            && MonsterDefinitionId.TryCreate(definitionId, out MonsterDefinitionId id)
            && content.TryGetMonster(id, out ClientMonster? monster)
                ? monster
                : null;
    }

    /// <summary>
    ///     The colour of the whole body a monster or an NPC is drawn in (Prototype Content §2); null for its model's own.
    /// </summary>
    public static Color? BodyTintOf(ClientContent? content, EntityKind kind, string definitionId)
    {
        if (kind == EntityKind.Npc)
        {
            return content != null
                && NpcDefinitionId.TryCreate(definitionId, out NpcDefinitionId id)
                && content.TryGetNpc(id, out ClientNpc? npc)
                && npc != null
                    ? npc.Tint
                    : null;
        }

        return MonsterOf(content, kind, definitionId)?.Tint;
    }

    public static string ForEntity(ClientContent? content, EntityKind kind, string definitionId)
    {
        switch (kind)
        {
            case EntityKind.Player when JobDefinitionId.TryCreate(definitionId, out JobDefinitionId job):
                return ForJob(content, job);
            case EntityKind.Monster when MonsterOf(content, kind, definitionId) is ClientMonster monster:
                return monster.PrefabKey;
            case EntityKind.ItemDrop
                when content != null
                && ItemDefinitionId.TryCreate(definitionId, out ItemDefinitionId itemId)
                && content.TryGetItem(itemId, out ClientItem? item)
                && item != null:
                return item.ModelKey;
            case EntityKind.Npc
                when content != null
                && NpcDefinitionId.TryCreate(definitionId, out NpcDefinitionId npcId)
                && content.TryGetNpc(npcId, out ClientNpc? npc)
                && npc != null:
                return npc.PrefabKey;
            default:
                return string.Empty;
        }
    }
}
}
