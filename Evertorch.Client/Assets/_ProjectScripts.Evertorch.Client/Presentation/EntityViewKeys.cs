using Evertorch.Game;
using Evertorch.Protocol;

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

    public static string ForEntity(ClientContent? content, EntityKind kind, string definitionId)
    {
        switch (kind)
        {
            case EntityKind.Player when JobDefinitionId.TryCreate(definitionId, out JobDefinitionId job):
                return ForJob(content, job);
            case EntityKind.Monster
                when content != null
                && MonsterDefinitionId.TryCreate(definitionId, out MonsterDefinitionId id)
                && content.TryGetMonster(id, out ClientMonster? monster)
                && monster != null:
                return monster.PrefabKey;
            default:
                return string.Empty;
        }
    }
}
}
