using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     An item lying on the ground after a monster's death. It is seen through interest management like any entity,
///     but it never moves, so it is never in a snapshot, and it cannot be targeted.
/// </summary>
public sealed class ItemDropEntity : WorldEntity
{
    private static readonly WorldDirection DropFacing = new(0f, 1f);

    public ItemDropEntity(
        EntityId id,
        ItemDefinitionId item,
        uint amount,
        WorldPosition position,
        uint droppedTick,
        long expiresAtMs)
        : base(id, position, DropFacing, 0f, 0, 0f)
    {
        Item = item;
        Amount = amount;
        DroppedTick = droppedTick;
        ExpiresAtMs = expiresAtMs;
    }

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
