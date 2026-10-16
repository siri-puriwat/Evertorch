using System;
using System.Collections.Generic;
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
    public NpcEntity(
        EntityId id,
        NpcDefinition definition,
        NpcPlacement placement,
        NpcServices services,
        IReadOnlyList<RefineTable>? refineTables = null,
        IReadOnlyList<CardRemovalFees>? cardRemovalFees = null)
        : base(id, placement.Position, MovementModel.NormalizeOrZero(placement.Facing.X, placement.Facing.Z), 0f, 0, 0f)
    {
        Definition = definition;
        Services = services;
        RefineTables = refineTables ?? Array.Empty<RefineTable>();
        CardRemovalFees = cardRemovalFees ?? Array.Empty<CardRemovalFees>();
    }

    public NpcDefinition Definition { get; }

    /// <summary>
    ///     Built once, when the map loads: the NPC's content never changes while the server runs.
    /// </summary>
    public NpcServices Services { get; }

    /// <summary>
    ///     A refiner's tables in index order, built once at load and sent after <see cref="Services" />; empty for any
    ///     other NPC.
    /// </summary>
    public IReadOnlyList<RefineTable> RefineTables { get; }

    /// <summary>
    ///     A card remover's fees in their parts, built once at load and sent after <see cref="RefineTables" />; empty for
    ///     any other NPC.
    /// </summary>
    public IReadOnlyList<CardRemovalFees> CardRemovalFees { get; }

    public override EntityKind Kind => EntityKind.Npc;

    public override string DefinitionId => Definition.Id.Value;
}
}
