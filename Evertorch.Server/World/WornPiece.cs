using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     One worn piece as <see cref="WornEquipment" /> reads it: its position, its item, its item's refine table, and its
///     refine level.
/// </summary>
public readonly struct WornPiece
{
    public WornPiece(EquipmentSlot slot, ItemDefinition item, RefineTableDefinition? table, int refineLevel)
    {
        Slot = slot;
        Item = item;
        Table = table;
        RefineLevel = refineLevel;
    }

    public EquipmentSlot Slot { get; }

    public ItemDefinition Item { get; }

    /// <summary>The item's refine table; null when it names none.</summary>
    public RefineTableDefinition? Table { get; }

    public int RefineLevel { get; }
}
}
