using System.Collections.Generic;
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

    /// <summary>
    ///     The item a player wears in each of the eight positions, in position order from the weapon, default where none,
    ///     from the appearance that follows its spawn and each change the server announces (Network Protocol §9); all
    ///     default for every other kind.
    /// </summary>
    public IReadOnlyList<ItemDefinitionId> Appearance { get; internal set; } = new ItemDefinitionId[8];

    /// <summary>The revision of <see cref="Appearance" />, which an inspection read at another is stale against.</summary>
    public uint AppearanceRevision { get; internal set; }

    /// <summary>The item ID of the weapon a player wears; empty when it wears none and for every other kind.</summary>
    public string WornWeapon => Appearance[0].Value;

    /// <summary>
    ///     A player's character name from its spawn; empty for every other kind (Network Protocol §6).
    /// </summary>
    public string Name { get; internal set; } = string.Empty;

    public RemoteEntityBuffer Buffer { get; } = new();
}
}
