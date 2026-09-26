using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     An NPC its map places when it loads (Gameplay Systems §6.1). Interest management shows and hides it like any
///     entity, but it never moves or dies, so it is never in a snapshot, and it cannot be targeted. Every client that
///     sees it spawn is told its services next.
/// </summary>
public sealed class NpcEntity : WorldEntity
{
    public NpcEntity(EntityId id, NpcDefinition definition, NpcPlacement placement, NpcServices services)
        : base(id, placement.Position, MovementModel.NormalizeOrZero(placement.Facing.X, placement.Facing.Z), 0f, 0, 0f)
    {
        Definition = definition;
        Services = services;
    }

    public NpcDefinition Definition { get; }

    /// <summary>
    ///     Built once, when the map loads: the NPC's content never changes while the server runs.
    /// </summary>
    public NpcServices Services { get; }

    public override EntityKind Kind => EntityKind.Npc;

    public override string DefinitionId => Definition.Id.Value;
}
}
