using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One of the player's listings (Network Protocol §6): its ID, its item, the units left and sold, the coins waiting
///     for its seller, which are its sales less the tax, and the seconds it sells for, 0 once it expired.
/// </summary>
public readonly struct MarketListingEntry
{
    public MarketListingEntry(
        long listing,
        ItemDefinitionId item,
        uint unitsLeft,
        uint unitsSold,
        uint coinsWaiting,
        uint secondsLeft)
    {
        if (listing <= 0 || item.Value.Length == 0 || (unitsLeft == 0 && unitsSold == 0))
        {
            throw new ArgumentException("A listing is at least 1, names its item, and has units left or sold.");
        }

        Listing = listing;
        Item = item;
        UnitsLeft = unitsLeft;
        UnitsSold = unitsSold;
        CoinsWaiting = coinsWaiting;
        SecondsLeft = secondsLeft;
    }

    public long Listing { get; }

    public ItemDefinitionId Item { get; }

    public uint UnitsLeft { get; }

    public uint UnitsSold { get; }

    /// <summary>
    ///     What collecting it would pay now: its sales less the tax.
    /// </summary>
    public uint CoinsWaiting { get; }

    public uint SecondsLeft { get; }

    internal int GetEncodedLength()
    {
        return sizeof(long) + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + 4 * sizeof(uint);
    }

    internal void Write(ref WireWriter writer)
    {
        writer.WriteInt64(Listing);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(UnitsLeft);
        writer.WriteUInt32(UnitsSold);
        writer.WriteUInt32(CoinsWaiting);
        writer.WriteUInt32(SecondsLeft);
    }

    // A listing was made with at least one unit, so it has units left or sold.
    internal static bool TryRead(ref WireReader reader, out MarketListingEntry entry)
    {
        entry = default;
        if (!reader.TryReadInt64(out long listing)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint unitsLeft)
            || !reader.TryReadUInt32(out uint unitsSold)
            || !reader.TryReadUInt32(out uint coinsWaiting)
            || !reader.TryReadUInt32(out uint secondsLeft)
            || listing <= 0
            || (unitsLeft == 0 && unitsSold == 0)
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        entry = new MarketListingEntry(listing, item, unitsLeft, unitsSold, coinsWaiting, secondsLeft);
        return true;
    }
}
}
