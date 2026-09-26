using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What the player is told when the server refuses one of their commands (Prototype Content §2), in the client's
///     own words.
/// </summary>
public static class RejectionMessages
{
    public static string Describe(CommandRejectionReason reason)
    {
        switch (reason)
        {
            case CommandRejectionReason.InvalidTarget:
                return "That target is not there any more.";
            case CommandRejectionReason.OutOfRange:
                return "That is too far away.";
            case CommandRejectionReason.NotAllowedNow:
                return "You cannot do that right now.";
            case CommandRejectionReason.LootPriority:
                return "Another player may take that drop first.";
            case CommandRejectionReason.InventoryFull:
                return "Your inventory is full.";
            case CommandRejectionReason.ServiceUnavailable:
                return "The server cannot save right now. Try again in a moment.";
            case CommandRejectionReason.Busy:
                return "That drop is already being picked up. Try again in a moment.";
            case CommandRejectionReason.NotEnoughSp:
                return "Not enough SP.";
            case CommandRejectionReason.ItemActionInFlight:
                return "Your last item action is still going through. Try again in a moment.";
            default:
                return "The server refused that.";
        }
    }
}
}
