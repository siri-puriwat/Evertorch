using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What the client knows about an entity it does not control.
/// </summary>
public sealed class RemoteEntity
{
    public RemoteEntity(
        EntityId entity,
        EntityKind kind,
        string definitionId,
        EntityStateFlags stateFlags,
        ushort healthPermille)
    {
        Entity = entity;
        Kind = kind;
        DefinitionId = definitionId;
        StateFlags = stateFlags;
        HealthPermille = healthPermille;
    }

    public EntityId Entity { get; }

    public EntityKind Kind { get; }

    public string DefinitionId { get; }

    public EntityStateFlags StateFlags { get; internal set; }

    public bool IsDead => (StateFlags & EntityStateFlags.Dead) != 0;

    /// <summary>
    ///     A monster's HP in thousandths of its maximum, as the server last reported it; 0 for other kinds.
    /// </summary>
    public ushort HealthPermille { get; internal set; }

    public RemoteEntityBuffer Buffer { get; } = new();
}
}
