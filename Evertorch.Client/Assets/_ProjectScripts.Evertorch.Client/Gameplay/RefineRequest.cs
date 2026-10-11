using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     A refine the client asked for, kept by its command sequence so a refusal can say what was refused: the Smith,
///     the row, its item, and the level the client showed for it.
/// </summary>
public readonly struct RefineRequest
{
    public RefineRequest(EntityId npc, long inventoryItem, ItemDefinitionId item, byte level)
    {
        Npc = npc;
        InventoryItem = inventoryItem;
        Item = item;
        Level = level;
    }

    public EntityId Npc { get; }

    public long InventoryItem { get; }

    public ItemDefinitionId Item { get; }

    public byte Level { get; }
}
}
