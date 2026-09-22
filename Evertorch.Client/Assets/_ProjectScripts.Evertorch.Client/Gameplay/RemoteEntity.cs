using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What the client knows about an entity it does not control.
/// </summary>
public sealed class RemoteEntity
{
    public RemoteEntity(EntityId entity, EntityKind kind, string definitionId, EntityStateFlags stateFlags)
    {
        Entity = entity;
        Kind = kind;
        DefinitionId = definitionId;
        StateFlags = stateFlags;
    }

    public EntityId Entity { get; }

    public EntityKind Kind { get; }

    public string DefinitionId { get; }

    public EntityStateFlags StateFlags { get; internal set; }

    public RemoteEntityBuffer Buffer { get; } = new();
}
}
