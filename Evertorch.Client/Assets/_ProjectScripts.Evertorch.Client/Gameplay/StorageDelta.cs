using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     What one committed change of storage moved: how many of an item went in, or out when negative, the fee a
///     deposit cost, and the row's refine level.
/// </summary>
public readonly struct StorageDelta
{
    public StorageDelta(ItemDefinitionId item, long quantity, uint depositFee, byte refineLevel = 0)
    {
        Item = item;
        Quantity = quantity;
        DepositFee = depositFee;
        RefineLevel = refineLevel;
    }

    public ItemDefinitionId Item { get; }

    public long Quantity { get; }

    public uint DepositFee { get; }

    public byte RefineLevel { get; }
}
}
