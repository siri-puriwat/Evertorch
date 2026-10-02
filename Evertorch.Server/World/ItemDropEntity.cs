using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     An item lying on the ground after a monster's death. It is seen through interest management like any entity,
///     but it never moves, so it is never in a snapshot, and it cannot be targeted. It can be picked up
///     (Gameplay Systems §11).
/// </summary>
public sealed class ItemDropEntity : WorldEntity
{
    private static readonly WorldDirection DropFacing = new(0f, 1f);

    public ItemDropEntity(
        EntityId id,
        Guid dropId,
        ItemDefinitionId item,
        uint amount,
        WorldPosition position,
        uint droppedTick,
        long expiresAtMs,
        CharacterId priority,
        int priorityMs = PickupSystem.LootPriorityMs)
        : base(id, position, DropFacing, 0f, 0, 0f)
    {
        DropId = dropId;
        Priority = priority;
        PriorityMs = priorityMs;
        Item = item;
        Amount = amount;
        DroppedTick = droppedTick;
        ExpiresAtMs = expiresAtMs;
    }

    /// <summary>
    ///     The drop's durable identity, and the operation ID of its pickup (Persistence §5).
    /// </summary>
    public Guid DropId { get; }

    /// <summary>
    ///     The character whose hit killed the monster, who alone may pick the drop up for a while; default for none.
    /// </summary>
    public CharacterId Priority { get; }

    /// <summary>
    ///     How long <see cref="Priority" /> alone may pick the drop up: a kill's loot for 3 s, a boss's prize at its
    ///     most valuable player's feet for 10 s.
    /// </summary>
    public int PriorityMs { get; }

    /// <summary>
    ///     The character whose pickup of this drop is being committed; default while none is. A reserved drop does
    ///     not expire and cannot be picked up by anyone else.
    /// </summary>
    public CharacterId ReservedBy { get; set; }

    public bool IsReserved => ReservedBy != default;

    public ItemDefinitionId Item { get; }

    public uint Amount { get; }

    /// <summary>
    ///     Clients that see the drop on this tick see it land, and are told its amount.
    /// </summary>
    public uint DroppedTick { get; }

    public long ExpiresAtMs { get; }

    public override EntityKind Kind => EntityKind.ItemDrop;

    public override string DefinitionId => Item.Value;
}
}
