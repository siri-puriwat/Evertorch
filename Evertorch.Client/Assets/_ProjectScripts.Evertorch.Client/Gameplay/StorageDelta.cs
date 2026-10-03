using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     What one committed change of storage moved: how many of an item went in, or out when negative, and the fee a
///     deposit cost.
/// </summary>
public readonly struct StorageDelta
{
    public StorageDelta(ItemDefinitionId item, long quantity, uint depositFee)
    {
        Item = item;
        Quantity = quantity;
        DepositFee = depositFee;
    }

    public ItemDefinitionId Item { get; }

    public long Quantity { get; }

    public uint DepositFee { get; }
}
}
