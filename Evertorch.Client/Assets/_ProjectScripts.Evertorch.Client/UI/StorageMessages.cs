using System;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of the Storekeeper (Prototype Content §2): what a committed change moved as a grey line in the chat
///     log, the fee, and the refusals of its commands.
/// </summary>
public static class StorageMessages
{
    /// <summary>
    ///     "Deposited Slime Gel x 10 for 20 coins." or "Withdrew Ash Staff."
    /// </summary>
    public static string Describe(StorageDelta delta, ClientContent? content)
    {
        string name = ShopMessages.ItemName(content, delta.Item);
        long count = Math.Abs(delta.Quantity);
        string units = count == 1 ? name : $"{name} x {count}";
        if (delta.Quantity < 0)
        {
            return $"Withdrew {units}.";
        }

        return delta.DepositFee > 0
            ? $"Deposited {units} for {ShopMessages.Coins(delta.DepositFee)}."
            : $"Deposited {units}.";
    }

    /// <summary>
    ///     "Each deposit costs 20 coins"
    /// </summary>
    public static string Fee(uint fee)
    {
        return fee == 0 ? "Deposits are free" : $"Each deposit costs {ShopMessages.Coins(fee)}";
    }

    /// <summary>
    ///     The words for a refused Storekeeper command; any reason without its own words reads as any refusal.
    /// </summary>
    public static string DescribeRefusal(StorageCommand command, CommandRejectionReason reason)
    {
        switch (command, reason)
        {
            case (StorageCommand.Open, CommandRejectionReason.ServiceUnavailable):
                return "Storage cannot be read right now.";
            case (StorageCommand.Deposit, CommandRejectionReason.NotEnoughCoins):
                return "You cannot pay the Storekeeper's fee.";
            case (StorageCommand.Deposit, CommandRejectionReason.InventoryFull):
                return "Storage is full.";
            case (StorageCommand.Deposit, CommandRejectionReason.NotAllowedNow):
                return "You cannot store that.";
            case (StorageCommand.Withdraw, CommandRejectionReason.InvalidTarget):
                return "That is no longer in storage.";
            default:
                return RejectionMessages.Describe(reason);
        }
    }
}
}
