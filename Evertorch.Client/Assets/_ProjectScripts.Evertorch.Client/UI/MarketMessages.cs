using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of the Broker (Prototype Content §2): the notices of a listing, a sale, and a collection as grey lines in
///     the chat log, the rows of the board, the listings, and the bag, what a first press asks to confirm, and the
///     refusals of its commands. A purchase keeps the shop's line, read from its change.
/// </summary>
public static class MarketMessages
{
    private const uint SecondsPerMinute = 60;
    private const uint SecondsPerHour = 60 * SecondsPerMinute;
    private const uint SecondsPerDay = 24 * SecondsPerHour;

    /// <summary>
    ///     "Listed Gloom Dust x 10 for a fee of 2 coins.", "Your Gloom Dust x 4 sold for 112 coins.", or "Collected 102
    ///     coins and Gloom Dust x 6."
    /// </summary>
    public static string Describe(MarketEvent notice, ClientContent? content)
    {
        string units = Units(content, notice.Item, notice.Quantity);
        switch (notice.Kind)
        {
            case MarketEventKind.Listed:
                return $"Listed {units} for a fee of {ShopMessages.Coins(notice.Coins)}.";
            case MarketEventKind.Sold:
                return $"Your {units} sold for {ShopMessages.Coins(notice.Coins)}.";
            default:
                if (notice.Quantity == 0)
                {
                    return $"Collected {ShopMessages.Coins(notice.Coins)}.";
                }

                return notice.Coins == 0
                    ? $"Collected {units}."
                    : $"Collected {ShopMessages.Coins(notice.Coins)} and {units}.";
        }
    }

    /// <summary>
    ///     "Buy Gloom Dust: 28 coins, 6 for sale", or "Gloom Dust: 28 coins, none for sale" when there is nothing to buy.
    /// </summary>
    public static string Entry(MarketBoardEntry entry, ClientContent? content)
    {
        string name = ShopMessages.ItemName(content, entry.Item);
        return entry.Stock == 0
            ? $"{name}: {ShopMessages.Coins(entry.Price)}, none for sale"
            : $"Buy {name}: {ShopMessages.Coins(entry.Price)}, {entry.Stock} for sale";
    }

    /// <summary>
    ///     "Press again to buy Gloom Dust x 4 for 112 coins"
    /// </summary>
    public static string BuyPrompt(ItemDefinitionId item, uint quantity, uint price, ClientContent? content)
    {
        return $"Press again to buy {Units(content, item, quantity)} for {ShopMessages.Coins((long)quantity * price)}";
    }

    /// <summary>
    ///     "Collect Gloom Dust: 6 left, 4 sold, 102 coins waiting, 2d 23h left", or "..., expired" once it sells no more.
    /// </summary>
    public static string Listing(MarketListingEntry listing, uint secondsLeft, ClientContent? content)
    {
        string name = ShopMessages.ItemName(content, listing.Item);
        string time = secondsLeft == 0 ? "expired" : $"{TimeLeft(secondsLeft)} left";
        return $"Collect {name}: {listing.UnitsLeft} left, {listing.UnitsSold} sold, "
            + $"{ShopMessages.Coins(listing.CoinsWaiting)} waiting, {time}";
    }

    /// <summary>
    ///     "Press again to end the Gloom Dust listing early"
    /// </summary>
    public static string CollectPrompt(ItemDefinitionId item, ClientContent? content)
    {
        return $"Press again to end the {ShopMessages.ItemName(content, item)} listing early";
    }

    /// <summary>
    ///     "List Gloom Dust x 10"
    /// </summary>
    public static string BagRow(ItemDefinitionId item, uint quantity, ClientContent? content)
    {
        return $"List {Units(content, item, quantity)}";
    }

    /// <summary>
    ///     "Press again to list Gloom Dust x 4 for a fee of 1 coin"
    /// </summary>
    public static string ListPrompt(ItemDefinitionId item, uint quantity, long fee, ClientContent? content)
    {
        return $"Press again to list {Units(content, item, quantity)} for a fee of {ShopMessages.Coins(fee)}";
    }

    /// <summary>
    ///     "2d 23h", "5h 20m", "4m 10s", or "45s".
    /// </summary>
    public static string TimeLeft(uint seconds)
    {
        if (seconds >= SecondsPerDay)
        {
            return $"{seconds / SecondsPerDay}d {seconds % SecondsPerDay / SecondsPerHour}h";
        }

        if (seconds >= SecondsPerHour)
        {
            return $"{seconds / SecondsPerHour}h {seconds % SecondsPerHour / SecondsPerMinute}m";
        }

        return seconds >= SecondsPerMinute
            ? $"{seconds / SecondsPerMinute}m {seconds % SecondsPerMinute}s"
            : $"{seconds}s";
    }

    /// <summary>
    ///     The words for a refused Broker command; any reason without its own words reads as any refusal.
    /// </summary>
    public static string DescribeRefusal(MarketCommand command, CommandRejectionReason reason)
    {
        switch (command, reason)
        {
            case (MarketCommand.Open, CommandRejectionReason.ServiceUnavailable):
                return "The board cannot be read right now.";
            case (MarketCommand.List, CommandRejectionReason.NotAllowedNow):
                return "You cannot list that.";
            case (MarketCommand.List, CommandRejectionReason.NotEnoughCoins):
                return "You cannot pay the listing's fee.";
            case (MarketCommand.List, CommandRejectionReason.RequirementNotMet):
                return $"You already have {MarketTerms.MaxListings} listings.";
            case (MarketCommand.Buy, CommandRejectionReason.InvalidTarget):
                return "The board changed: the price went up, or too few are left.";
            case (MarketCommand.Buy, CommandRejectionReason.NotAllowedNow):
                return "You cannot buy that many at once.";
            case (MarketCommand.Buy, CommandRejectionReason.NotEnoughCoins):
                return "You do not have enough coins for that.";
            case (MarketCommand.Collect, CommandRejectionReason.InvalidTarget):
                return "That listing is gone.";
            case (MarketCommand.Collect, CommandRejectionReason.CoinCapReached):
                return "You cannot hold that many coins.";
            case (MarketCommand.Collect, CommandRejectionReason.InventoryFull):
                return "Your bag has no room for what is left.";
            case (MarketCommand.Collect, CommandRejectionReason.ServiceUnavailable):
                return "That listing cannot be collected right now.";
            default:
                return RejectionMessages.Describe(reason);
        }
    }

    private static string Units(ClientContent? content, ItemDefinitionId item, uint quantity)
    {
        string name = ShopMessages.ItemName(content, item);
        return quantity == 1 ? name : $"{name} x {quantity}";
    }
}
}
