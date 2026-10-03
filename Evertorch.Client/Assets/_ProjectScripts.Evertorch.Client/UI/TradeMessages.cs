using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of a face-to-face trade (Prototype Content §2): what happened to it as a grey line in the chat log, the
///     request's question, the window's lines, and the refusals of its commands.
/// </summary>
public static class TradeMessages
{
    public const string Held = "You can't move while trading.";

    /// <summary>
    ///     "Anna wants to trade with you."
    /// </summary>
    public static string Request(string requester)
    {
        return $"{requester} wants to trade with you.";
    }

    /// <summary>
    ///     The line for <paramref name="message" />; null for one the log does not show.
    /// </summary>
    public static string? Describe(TradeEvent message)
    {
        return message.Kind switch
        {
            TradeEventKind.Requested => Request(message.Name),
            TradeEventKind.Declined => $"{message.Name} declined to trade.",
            TradeEventKind.Expired => $"{message.Name} did not answer.",
            TradeEventKind.Opened => $"Trading with {message.Name}.",
            TradeEventKind.Cancelled => "The trade was cancelled.",
            TradeEventKind.Completed => "Trade complete.",
            TradeEventKind.Failed => Failure(message.Name, message.Reason),
            TradeEventKind.Unsaved => "The server cannot save right now. Confirm again.",
            _ => null
        };
    }

    private static string Failure(string name, CommandRejectionReason reason)
    {
        return reason switch
        {
            CommandRejectionReason.InventoryFull => $"The trade failed: {name} cannot hold it all.",
            CommandRejectionReason.CoinCapReached => $"The trade failed: {name} cannot hold that many coins.",
            CommandRejectionReason.ServiceUnavailable => "The trade failed: the server could not save it.",
            _ => $"The trade failed: {name}'s offer no longer holds."
        };
    }

    /// <summary>
    ///     A side's state as the window shows it: "Locked", "Confirmed", or nothing.
    /// </summary>
    public static string State(TradeSide side)
    {
        return side.IsConfirmed ? "Confirmed" : side.IsLocked ? "Locked" : string.Empty;
    }

    /// <summary>
    ///     A side's rows and coins, one line each: "Slime Gel x 4", "Iron Sword", "100 coins".
    /// </summary>
    public static IReadOnlyList<string> Lines(TradeSide side, ClientContent? content)
    {
        var lines = new List<string>(side.Entries.Count + 1);
        foreach (TradeEntry entry in side.Entries)
        {
            string name = ShopMessages.ItemName(content, entry.Item);
            string refined = entry.RefineLevel > 0 ? $"+{entry.RefineLevel} " : string.Empty;
            lines.Add(entry.Quantity == 1 ? $"{refined}{name}" : $"{refined}{name} x {entry.Quantity}");
        }

        if (side.Coins > 0)
        {
            lines.Add(ShopMessages.Coins(side.Coins));
        }

        return lines;
    }

    /// <summary>
    ///     The words for a refused trade command: what it asked of <paramref name="name" /> decides what the reason means;
    ///     any reason without its own words reads as any refusal.
    /// </summary>
    public static string DescribeRefusal(TradeCommand command, string name, CommandRejectionReason reason)
    {
        switch (command, reason)
        {
            case (TradeCommand.Request, CommandRejectionReason.InvalidTarget):
                return $"{name} is not online.";
            case (TradeCommand.Request, CommandRejectionReason.OutOfRange):
                return $"{name} is too far away to trade.";
            case (TradeCommand.Request, CommandRejectionReason.NotAllowedNow):
                return $"{name} cannot trade now.";
            case (TradeCommand.Reply, CommandRejectionReason.InvalidTarget):
                return $"{name}'s request is no longer open.";
            case (TradeCommand.Reply, CommandRejectionReason.OutOfRange):
                return $"{name} is too far away to trade.";
            case (TradeCommand.Reply, CommandRejectionReason.NotAllowedNow):
                return $"You cannot trade with {name} now.";
            case (TradeCommand.Offer, CommandRejectionReason.InvalidTarget):
                return "That item is not in your bag any more.";
            case (TradeCommand.Offer, CommandRejectionReason.NotAllowedNow):
                return "You cannot offer that.";
            case (TradeCommand.Confirm, CommandRejectionReason.NotAllowedNow):
                return "Both offers must be locked, and something offered, before you trade.";
            case (TradeCommand.Lock, CommandRejectionReason.NotAllowedNow):
            case (TradeCommand.Cancel, CommandRejectionReason.NotAllowedNow):
                return "You are not trading.";
            default:
                return RejectionMessages.Describe(reason);
        }
    }
}
}
