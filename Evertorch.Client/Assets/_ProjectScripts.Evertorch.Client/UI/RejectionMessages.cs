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
        return Describe(reason, false);
    }

    /// <summary>
    ///     The words for a refused party command (Prototype Content §2): what it asked of <paramref name="name" />, the
    ///     character it named, decides what the reason means; any reason without its own words reads as any refusal.
    /// </summary>
    public static string DescribeParty(PartyCommand command, string name, CommandRejectionReason reason)
    {
        switch (command, reason)
        {
            case (PartyCommand.Invite, CommandRejectionReason.InvalidTarget):
                return $"{name} is not online.";
            case (PartyCommand.Invite, CommandRejectionReason.NotAllowedNow):
                return $"{name} cannot be invited now.";
            case (PartyCommand.Invite, CommandRejectionReason.RequirementNotMet):
                return $"{name} cannot join: your party is full or holds a character of the same account.";
            case (PartyCommand.Reply, CommandRejectionReason.InvalidTarget):
                return $"{name}'s invite is no longer open.";
            case (PartyCommand.Reply, CommandRejectionReason.NotAllowedNow):
                return $"You cannot join {name}'s party now.";
            case (PartyCommand.Reply, CommandRejectionReason.RequirementNotMet):
                return $"{name}'s party is full, or holds a character of your account.";
            case (PartyCommand.Leave, CommandRejectionReason.NotAllowedNow):
                return "You are not in a party.";
            case (PartyCommand.Kick, CommandRejectionReason.InvalidTarget):
            case (PartyCommand.Lead, CommandRejectionReason.InvalidTarget):
                return $"{name} is not in your party.";
            case (PartyCommand.Kick, CommandRejectionReason.NotAllowedNow):
            case (PartyCommand.Lead, CommandRejectionReason.NotAllowedNow):
                return "Only the party's leader can do that.";
            case (_, CommandRejectionReason.Busy):
                return "Your party's last change is still going through. Try again in a moment.";
            default:
                return Describe(reason);
        }
    }

    /// <summary>
    ///     The words for <paramref name="reason" />; an equip refused for a requirement is a weapon the character's job
    ///     cannot wield (Gameplay Systems §11.1).
    /// </summary>
    public static string Describe(CommandRejectionReason reason, bool isEquip)
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
            case CommandRejectionReason.NotEnoughCoins:
                return "You do not have enough coins.";
            case CommandRejectionReason.CoinCapReached:
                return "You cannot hold any more coins.";
            case CommandRejectionReason.NotEnoughPoints:
                return "You do not have enough points.";
            case CommandRejectionReason.RequirementNotMet:
                return isEquip ? "Your job cannot wield that." : "That cannot be done.";
            default:
                return "The server refused that.";
        }
    }
}
}
