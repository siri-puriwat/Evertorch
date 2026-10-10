using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     What happened to one of the player's listings (Network Protocol §6, §9): listed and collected to the player who
///     did it, after the commit's inventory change; sold to its seller, whom a connection controls in the world, never
///     naming the buyer.
/// </summary>
public sealed class MarketEvent
{
    public const int MaxEncodedLength =
        sizeof(ushort) + sizeof(byte) + sizeof(long) + sizeof(ushort) + ProtocolLimits.MaxDefinitionIdBytes
        + 2 * sizeof(uint);

    public MarketEvent(MarketEventKind kind, long listing, ItemDefinitionId item, uint quantity, uint coins)
    {
        if (item.Value.Length == 0 || !IsValid(kind, listing, quantity, coins))
        {
            throw new ArgumentException(
                "A notice names its item and a listing of at least 1; a listing and a sale carry units and coins, a "
                + "collection one of them.");
        }

        Kind = kind;
        Listing = listing;
        Item = item;
        Quantity = quantity;
        Coins = coins;
    }

    public MarketEventKind Kind { get; }

    public long Listing { get; }

    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     The units listed, sold, or given back.
    /// </summary>
    public uint Quantity { get; }

    /// <summary>
    ///     The fee of a listing, the gross of a sale, or the net of a collection.
    /// </summary>
    public uint Coins { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out MarketEvent? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MarketEvent)
            || !reader.TryReadByte(out byte kind)
            || !reader.TryReadInt64(out long listing)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadUInt32(out uint coins)
            || !reader.IsAtEnd
            || !IsValid((MarketEventKind)kind, listing, quantity, coins)
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        message = new MarketEvent((MarketEventKind)kind, listing, item, quantity, coins);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(byte)
            + sizeof(long)
            + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + 2 * sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MarketEvent);
        writer.WriteByte((byte)Kind);
        writer.WriteInt64(Listing);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Quantity);
        writer.WriteUInt32(Coins);
        return writer.Position;
    }

    private static bool IsValid(MarketEventKind kind, long listing, uint quantity, uint coins)
    {
        if (listing <= 0)
        {
            return false;
        }

        return kind switch
        {
            MarketEventKind.Listed => quantity > 0 && coins > 0,
            MarketEventKind.Sold => quantity > 0 && coins > 0,
            MarketEventKind.Collected => quantity > 0 || coins > 0,
            _ => false
        };
    }
}
}
